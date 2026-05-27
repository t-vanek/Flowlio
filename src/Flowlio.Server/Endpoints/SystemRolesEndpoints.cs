using Flowlio.Application.Abstractions;
using Flowlio.Domain;
using Flowlio.Infrastructure.Identity;
using Flowlio.Server.Auth;
using Flowlio.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static Flowlio.Server.Auth.MemberAuthorization;

namespace Flowlio.Server.Endpoints;

/// <summary>
/// Management of cross-family system roles and their <see cref="SystemPermission"/>s. Viewing needs
/// <see cref="SystemPermission.ViewUsers"/>; editing needs <see cref="SystemPermission.ManageSystemRoles"/>.
/// The built-in administrator role always holds every permission and cannot be edited or deleted.
/// </summary>
public static class SystemRolesEndpoints
{
    public static void MapSystemRolesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization(AdminRoles.AdminPolicy)
            .AddEndpointFilter<Validation.ValidationEndpointFilter>();
        group.MapGet("/system-roles", GetRoles);
        group.MapPost("/system-roles", CreateRole);
        group.MapPut("/system-roles/{roleId:guid}", RenameRole);
        group.MapPut("/system-roles/{roleId:guid}/permissions", SetPermissions);
        group.MapDelete("/system-roles/{roleId:guid}", DeleteRole);
    }

    private static async Task<IResult> GetRoles(
        RoleManager<IdentityRole<Guid>> roleManager, UserManager<ApplicationUser> userManager,
        IAppDbContext db, ICurrentSystemAccess sys, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.ViewUsers, ct))
            return Forbidden();

        var roles = await roleManager.Roles.OrderBy(r => r.Name).ToListAsync(ct);
        var grants = await db.SystemRolePermissions.ToListAsync(ct);
        var byRole = grants
            .GroupBy(g => g.RoleId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<SystemPermission>)g.Select(x => x.Permission).ToList());

        var allPermissions = Enum.GetValues<SystemPermission>().ToList();
        var dtos = new List<SystemRoleDto>();
        foreach (var role in roles)
        {
            var isAdmin = role.Name == SystemRoles.Administrator;
            var userCount = (await userManager.GetUsersInRoleAsync(role.Name!)).Count;
            dtos.Add(new SystemRoleDto
            {
                RoleId = role.Id,
                Name = role.Name!,
                IsAdministrator = isAdmin,
                Permissions = isAdmin ? allPermissions : byRole.TryGetValue(role.Id, out var ps) ? ps : [],
                UserCount = userCount,
            });
        }

        return Results.Ok(new SystemRolesDto { AllPermissions = allPermissions, Roles = dtos });
    }

    private static async Task<IResult> CreateRole(
        CreateSystemRoleRequest request, RoleManager<IdentityRole<Guid>> roleManager, ICurrentSystemAccess sys,
        IAuditLog audit, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.ManageSystemRoles, ct))
            return Forbidden();

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return Results.BadRequest("Název role je povinný.");
        if (await roleManager.RoleExistsAsync(name))
            return Results.BadRequest("Role s tímto názvem už existuje.");

        var role = new IdentityRole<Guid>(name);
        var result = await roleManager.CreateAsync(role);
        if (!result.Succeeded)
            return Results.BadRequest(string.Join(" ", result.Errors.Select(e => e.Description)));

        await audit.RecordAsync("system-role.create", "SystemRole", role.Id.ToString(), name, cancellationToken: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RenameRole(
        Guid roleId, RenameSystemRoleRequest request, RoleManager<IdentityRole<Guid>> roleManager, ICurrentSystemAccess sys,
        IAuditLog audit, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.ManageSystemRoles, ct))
            return Forbidden();

        var role = await roleManager.FindByIdAsync(roleId.ToString());
        if (role is null)
            return Results.NotFound();
        if (role.Name == SystemRoles.Administrator)
            return Results.BadRequest("Roli administrátora nelze přejmenovat.");

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return Results.BadRequest("Název role je povinný.");

        var oldName = role.Name;
        role.Name = name;
        var result = await roleManager.UpdateAsync(role);
        if (!result.Succeeded)
            return Results.BadRequest(string.Join(" ", result.Errors.Select(e => e.Description)));

        await audit.RecordAsync("system-role.rename", "SystemRole", roleId.ToString(), name,
            details: $"Přejmenováno z „{oldName}“", cancellationToken: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetPermissions(
        Guid roleId, UpdateSystemRolePermissionsRequest request, RoleManager<IdentityRole<Guid>> roleManager,
        IAppDbContext db, ICurrentSystemAccess sys, IAuditLog audit, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.ManageSystemRoles, ct))
            return Forbidden();

        var role = await roleManager.FindByIdAsync(roleId.ToString());
        if (role is null)
            return Results.NotFound();
        if (role.Name == SystemRoles.Administrator)
            return Results.BadRequest("Oprávnění administrátora nelze měnit.");

        var requested = (request.Permissions ?? []).Distinct().ToHashSet();
        var existing = await db.SystemRolePermissions.Where(p => p.RoleId == roleId).ToListAsync(ct);
        var existingSet = existing.Select(p => p.Permission).ToHashSet();

        foreach (var stale in existing.Where(p => !requested.Contains(p.Permission)))
            db.SystemRolePermissions.Remove(stale);
        foreach (var added in requested.Where(p => !existingSet.Contains(p)))
            db.SystemRolePermissions.Add(new SystemRolePermission { RoleId = roleId, Permission = added });

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("system-role.permissions", "SystemRole", roleId.ToString(), role.Name,
            details: $"Oprávnění: {(requested.Count > 0 ? string.Join(", ", requested) : "žádná")}", cancellationToken: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteRole(
        Guid roleId, RoleManager<IdentityRole<Guid>> roleManager, ICurrentSystemAccess sys, IAuditLog audit, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.ManageSystemRoles, ct))
            return Forbidden();

        var role = await roleManager.FindByIdAsync(roleId.ToString());
        if (role is null)
            return Results.NotFound();
        if (role.Name == SystemRoles.Administrator)
            return Results.BadRequest("Roli administrátora nelze smazat.");

        var name = role.Name;
        var result = await roleManager.DeleteAsync(role);
        if (!result.Succeeded)
            return Results.BadRequest(string.Join(" ", result.Errors.Select(e => e.Description)));

        await audit.RecordAsync("system-role.delete", "SystemRole", roleId.ToString(), name, cancellationToken: ct);
        return Results.NoContent();
    }
}
