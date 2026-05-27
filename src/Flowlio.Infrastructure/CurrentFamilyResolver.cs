using Flowlio.Application.Abstractions;
using Flowlio.Application.Categories;
using Flowlio.Domain;
using Flowlio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flowlio.Infrastructure;

/// <summary>Resolves (and lazily provisions) the family for the authenticated user.</summary>
public sealed class CurrentFamilyResolver(ApplicationDbContext db, ICurrentUser user) : ICurrentFamily
{
    public async Task<Guid> RequireAsync(CancellationToken cancellationToken = default)
    {
        var userId = user.UserId
            ?? throw new InvalidOperationException("No authenticated user to resolve a family for.");

        var familyId = await db.FamilyMembers
            .Where(m => m.UserId == userId)
            .Select(m => (Guid?)m.FamilyId)
            .FirstOrDefaultAsync(cancellationToken);

        if (familyId is { } existing)
            return existing;

        var family = new Family { Name = "Naše rodina" };
        db.Families.Add(family);
        db.FamilyMembers.Add(new FamilyMember
        {
            FamilyId = family.Id,
            UserId = userId,
            DisplayName = "Vlastník",
            Role = MemberRole.Owner,
        });
        db.Categories.AddRange(DefaultCategories.Create(family.Id));
        await db.SaveChangesAsync(cancellationToken);

        return family.Id;
    }
}
