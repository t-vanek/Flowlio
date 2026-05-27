using Flowlio.Application.Abstractions;
using Flowlio.Application.Categories;
using Flowlio.Domain;
using Flowlio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flowlio.Infrastructure;

/// <summary>Resolves (and lazily provisions) the family for the authenticated user.</summary>
public sealed class CurrentFamilyResolver(ApplicationDbContext db, ICurrentUser user) : ICurrentFamily
{
    private FamilyMember? _member;
    private IReadOnlySet<Permission>? _permissions;

    public async Task<Guid> RequireAsync(CancellationToken cancellationToken = default) =>
        (await RequireMemberAsync(cancellationToken)).FamilyId;

    public async Task<FamilyMember> RequireMemberAsync(CancellationToken cancellationToken = default)
    {
        if (_member is not null)
            return _member;

        var userId = user.UserId
            ?? throw new InvalidOperationException("No authenticated user to resolve a family for.");

        var member = await db.FamilyMembers
            .FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);

        if (member is not null)
        {
            if (!member.IsActive)
                throw new FamilyAccessDeniedException("Váš přístup do rodiny byl pozastaven.");
            return _member = member;
        }

        var family = new Family { Name = "Naše rodina" };
        db.Families.Add(family);
        var owner = new FamilyMember
        {
            FamilyId = family.Id,
            UserId = userId,
            DisplayName = "Vlastník",
            Role = MemberRole.Owner,
        };
        db.FamilyMembers.Add(owner);
        db.Categories.AddRange(DefaultCategories.Create(family.Id));
        db.FamilyRolePermissions.AddRange(FamilyRolePermission.CreateDefaults(family.Id));
        await db.SaveChangesAsync(cancellationToken);

        return _member = owner;
    }

    public async Task<IReadOnlySet<Permission>> GetPermissionsAsync(CancellationToken cancellationToken = default)
    {
        if (_permissions is not null)
            return _permissions;

        var me = await RequireMemberAsync(cancellationToken);

        // Owners always hold every permission and cannot lock themselves out.
        if (me.Role == MemberRole.Owner)
            return _permissions = Enum.GetValues<Permission>().ToHashSet();

        var granted = await db.FamilyRolePermissions
            .Where(r => r.FamilyId == me.FamilyId && r.Role == me.Role)
            .Select(r => r.Permission)
            .ToListAsync(cancellationToken);

        return _permissions = granted.ToHashSet();
    }

    public async Task<bool> CanAsync(Permission permission, CancellationToken cancellationToken = default) =>
        (await GetPermissionsAsync(cancellationToken)).Contains(permission);
}
