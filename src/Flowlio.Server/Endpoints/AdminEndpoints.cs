using Flowlio.Application.Abstractions;
using Flowlio.Infrastructure.Identity;
using Flowlio.Server.Auth;
using Flowlio.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Flowlio.Server.Endpoints;

/// <summary>
/// System administration (cross-family). Restricted to the <see cref="AdminRoles.Administrator"/>
/// role: lets an administrator review every login account, grant/revoke admin, lock/unlock and delete.
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(AdminRoles.AdminPolicy);
        admin.MapGet("/users", GetUsers);
        admin.MapPost("/users/{userId:guid}/admin", SetAdmin);
        admin.MapPost("/users/{userId:guid}/locked", SetLocked);
        admin.MapDelete("/users/{userId:guid}", DeleteUser);
    }

    private static async Task<IReadOnlyList<AdminUserDto>> GetUsers(
        UserManager<ApplicationUser> userManager, IAppDbContext db, ICurrentUser current, CancellationToken ct)
    {
        var users = await userManager.Users.OrderBy(u => u.Email).ToListAsync(ct);
        var adminIds = (await userManager.GetUsersInRoleAsync(AdminRoles.Administrator)).Select(u => u.Id).ToHashSet();

        var memberships = await db.FamilyMembers
            .Where(m => m.UserId != null)
            .Select(m => new { UserId = m.UserId!.Value, FamilyName = m.Family!.Name })
            .ToListAsync(ct);
        var familiesByUser = memberships
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.FamilyName).Distinct().ToList());

        var now = DateTimeOffset.UtcNow;
        return users.Select(u => new AdminUserDto
        {
            Id = u.Id,
            Email = u.Email,
            DisplayName = u.DisplayName,
            IsAdmin = adminIds.Contains(u.Id),
            IsLockedOut = u.LockoutEnd is { } end && end > now,
            IsCurrentUser = u.Id == current.UserId,
            CreatedAt = u.CreatedAt,
            Families = familiesByUser.TryGetValue(u.Id, out var fams) ? fams : [],
        }).ToList();
    }

    private static async Task<IResult> SetAdmin(
        Guid userId, SetUserAdminRequest request,
        UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager, ICurrentUser current)
    {
        if (userId == current.UserId && !request.IsAdmin)
            return Results.BadRequest("Vlastní administrátorská práva nelze odebrat.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        if (!await roleManager.RoleExistsAsync(AdminRoles.Administrator))
            await roleManager.CreateAsync(new IdentityRole<Guid>(AdminRoles.Administrator));

        var inRole = await userManager.IsInRoleAsync(user, AdminRoles.Administrator);
        if (request.IsAdmin && !inRole)
            await userManager.AddToRoleAsync(user, AdminRoles.Administrator);
        else if (!request.IsAdmin && inRole)
            await userManager.RemoveFromRoleAsync(user, AdminRoles.Administrator);

        return Results.NoContent();
    }

    private static async Task<IResult> SetLocked(
        Guid userId, SetUserLockedRequest request, UserManager<ApplicationUser> userManager, ICurrentUser current)
    {
        if (userId == current.UserId)
            return Results.BadRequest("Vlastní účet nelze zamknout.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        if (request.IsLocked)
        {
            await userManager.SetLockoutEnabledAsync(user, true);
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        }
        else
        {
            await userManager.SetLockoutEndDateAsync(user, null);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteUser(
        Guid userId, UserManager<ApplicationUser> userManager, IAppDbContext db, ICurrentUser current, CancellationToken ct)
    {
        if (userId == current.UserId)
            return Results.BadRequest("Vlastní účet nelze smazat.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        // Detach the login from any family profiles so no dangling user id remains; the profiles
        // become inactive managed records that an owner can reassign or remove.
        var members = await db.FamilyMembers.Where(m => m.UserId == userId).ToListAsync(ct);
        foreach (var member in members)
        {
            member.UserId = null;
            member.IsActive = false;
        }
        await db.SaveChangesAsync(ct);

        var result = await userManager.DeleteAsync(user);
        return result.Succeeded
            ? Results.NoContent()
            : Results.BadRequest(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
