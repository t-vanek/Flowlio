using Flowlio.Application.Abstractions;
using Flowlio.Application.Categories;
using Flowlio.Domain;
using Flowlio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flowlio.Infrastructure;

/// <summary>Resolves (and lazily provisions) the family for the authenticated user.</summary>
public sealed class CurrentFamilyResolver(ApplicationDbContext db, ICurrentUser user) : ICurrentFamily
{
    public async Task<Guid> RequireAsync(CancellationToken cancellationToken = default) =>
        (await RequireMemberAsync(cancellationToken)).FamilyId;

    public async Task<FamilyMember> RequireMemberAsync(CancellationToken cancellationToken = default)
    {
        var userId = user.UserId
            ?? throw new InvalidOperationException("No authenticated user to resolve a family for.");

        var member = await db.FamilyMembers
            .FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);

        if (member is not null)
            return member;

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
        await db.SaveChangesAsync(cancellationToken);

        return owner;
    }
}
