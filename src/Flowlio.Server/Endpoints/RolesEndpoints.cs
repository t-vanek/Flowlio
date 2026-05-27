using Flowlio.Application.Abstractions;
using Flowlio.Domain;
using Flowlio.Server.Realtime;
using Flowlio.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using static Flowlio.Server.Auth.MemberAuthorization;

namespace Flowlio.Server.Endpoints;

/// <summary>Per-family editing of what each role may do. The Owner role is always all-powerful and immutable.</summary>
public static class RolesEndpoints
{
    private static readonly MemberRole[] AllRoles =
        [MemberRole.Owner, MemberRole.Adult, MemberRole.Viewer, MemberRole.Child];

    public static void MapRolesEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/roles", GetRoles);
        api.MapPut("/roles/{role}", UpdateRole);
    }

    private static async Task<IResult> GetRoles(IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (!await family.CanAsync(Permission.ManageRoles, ct))
            return Forbidden();

        var grants = await db.FamilyRolePermissions
            .Where(r => r.FamilyId == me.FamilyId)
            .ToListAsync(ct);
        var byRole = grants
            .GroupBy(r => r.Role)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Permission>)g.Select(x => x.Permission).ToList());

        var allPermissions = Enum.GetValues<Permission>().ToList();
        var roles = AllRoles.Select(role => new RolePermissionsDto
        {
            Role = role,
            Editable = role != MemberRole.Owner,
            Permissions = role == MemberRole.Owner
                ? allPermissions
                : byRole.TryGetValue(role, out var ps) ? ps : [],
        }).ToList();

        return Results.Ok(new FamilyRolesDto { AllPermissions = allPermissions, Roles = roles });
    }

    private static async Task<IResult> UpdateRole(
        string role, UpdateRolePermissionsRequest request, IAppDbContext db, ICurrentFamily family,
        IHubContext<NotificationsHub> hub, IAuditLog audit, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (!await family.CanAsync(Permission.ManageRoles, ct))
            return Forbidden();

        if (!Enum.TryParse<MemberRole>(role, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            return Results.BadRequest("Neznámá role.");
        if (parsed == MemberRole.Owner)
            return Results.BadRequest("Oprávnění vlastníka nelze měnit.");

        var requested = request.Permissions.Distinct().ToHashSet();

        var existing = await db.FamilyRolePermissions
            .Where(r => r.FamilyId == me.FamilyId && r.Role == parsed)
            .ToListAsync(ct);
        var existingSet = existing.Select(r => r.Permission).ToHashSet();

        foreach (var stale in existing.Where(r => !requested.Contains(r.Permission)))
            db.FamilyRolePermissions.Remove(stale);
        foreach (var added in requested.Where(p => !existingSet.Contains(p)))
            db.FamilyRolePermissions.Add(new FamilyRolePermission { FamilyId = me.FamilyId, Role = parsed, Permission = added });

        await db.SaveChangesAsync(ct);
        await hub.NotifyFamilyAsync(me.FamilyId, ct);
        await audit.RecordAsync("family-role.permissions", "FamilyRole", parsed.ToString(), parsed.ToString(), me.FamilyId,
            $"Oprávnění role {parsed}", ct);
        return Results.NoContent();
    }
}
