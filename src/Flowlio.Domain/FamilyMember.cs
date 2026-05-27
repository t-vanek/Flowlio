using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>
/// A person within a family. May be linked to an authenticated user (ASP.NET Identity) once they
/// accept an invitation, or exist as a guardian-managed profile (e.g. a young child) with no login.
/// </summary>
public class FamilyMember : AuditableEntity
{
    public Guid FamilyId { get; set; }
    public Family? Family { get; set; }

    /// <summary>
    /// Primary key of the ASP.NET Identity user, or <c>null</c> while the member is a pending
    /// invitation or a guardian-managed child without their own login.
    /// </summary>
    public Guid? UserId { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>Contact e-mail the invitation was (or will be) sent to. Optional for managed children.</summary>
    public string? Email { get; set; }

    public MemberRole Role { get; set; } = MemberRole.Adult;

    /// <summary>For <see cref="MemberRole.Child"/> members, the guardian (parent) member who controls them.</summary>
    public Guid? GuardianMemberId { get; set; }
    public FamilyMember? Guardian { get; set; }

    /// <summary>Child members controlled by this member.</summary>
    public ICollection<FamilyMember> Dependents { get; set; } = [];

    /// <summary>Per-account access grants (disponent / viewer) held by this member.</summary>
    public ICollection<AccountAccess> AccountAccesses { get; set; } = [];
}
