using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>Links an authenticated user (ASP.NET Identity) to a family with a role.</summary>
public class FamilyMember : AuditableEntity
{
    public Guid FamilyId { get; set; }
    public Family? Family { get; set; }

    /// <summary>Primary key of the ASP.NET Identity user. Identity lives in the infrastructure layer.</summary>
    public Guid UserId { get; set; }

    public required string DisplayName { get; set; }
    public MemberRole Role { get; set; } = MemberRole.Adult;

    /// <summary>Bank accounts owned by this member.</summary>
    public ICollection<BankAccount> Accounts { get; set; } = [];
}
