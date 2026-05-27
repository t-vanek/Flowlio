using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>
/// Grants a family member access to a bank account they do not own — typically a spouse acting as
/// an authorized user ("disponent"), or a read-only viewer.
/// </summary>
public class AccountAccess : AuditableEntity
{
    public Guid BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    public Guid FamilyMemberId { get; set; }
    public FamilyMember? Member { get; set; }

    public AccountAccessLevel Level { get; set; } = AccountAccessLevel.Disponent;
}
