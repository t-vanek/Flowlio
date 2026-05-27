using Flowlio.Application.Abstractions;
using Flowlio.Domain;
using Flowlio.Infrastructure.Identity;
using Flowlio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Flowlio.Infrastructure;

/// <summary>Resolves the current user's effective system permissions from their ASP.NET Identity roles.</summary>
public sealed class CurrentSystemAccess(
    ICurrentUser user,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    ApplicationDbContext db) : ICurrentSystemAccess
{
    private static readonly IReadOnlySet<SystemPermission> All = Enum.GetValues<SystemPermission>().ToHashSet();

    private IReadOnlySet<SystemPermission>? _cached;

    public async Task<IReadOnlySet<SystemPermission>> GetPermissionsAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
            return _cached;

        if (user.UserId is not { } userId)
            return _cached = new HashSet<SystemPermission>();

        var appUser = await userManager.FindByIdAsync(userId.ToString());
        if (appUser is null)
            return _cached = new HashSet<SystemPermission>();

        var roleNames = await userManager.GetRolesAsync(appUser);
        if (roleNames.Count == 0)
            return _cached = new HashSet<SystemPermission>();

        // The administrator role always holds every permission and is never stored as grants.
        if (roleNames.Contains(SystemRoles.Administrator))
            return _cached = All;

        var roleIds = await roleManager.Roles
            .Where(r => roleNames.Contains(r.Name!))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var granted = await db.SystemRolePermissions
            .Where(p => roleIds.Contains(p.RoleId))
            .Select(p => p.Permission)
            .ToListAsync(cancellationToken);

        return _cached = granted.ToHashSet();
    }

    public async Task<bool> CanAsync(SystemPermission permission, CancellationToken cancellationToken = default) =>
        (await GetPermissionsAsync(cancellationToken)).Contains(permission);

    public async Task<bool> HasAnyAsync(CancellationToken cancellationToken = default) =>
        (await GetPermissionsAsync(cancellationToken)).Count > 0;
}
