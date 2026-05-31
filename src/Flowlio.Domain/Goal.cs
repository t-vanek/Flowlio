using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>
/// A savings goal tied to a bank account. Progress is the account's current balance minus
/// <see cref="BaselineAmount"/> (the balance captured when the goal was created), measured against
/// <see cref="TargetAmount"/>. Amounts are in the linked account's currency.
/// </summary>
public class Goal : AuditableEntity, ISoftDeletable
{
    public Guid FamilyId { get; set; }

    public required string Name { get; set; }

    public Guid BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    /// <summary>How much to save (on top of the baseline), in the account's currency.</summary>
    public decimal TargetAmount { get; set; }

    /// <summary>The account balance when the goal was created; progress counts savings beyond it.</summary>
    public decimal BaselineAmount { get; set; }

    /// <summary>Optional deadline, used to derive the required monthly contribution.</summary>
    public DateOnly? TargetDate { get; set; }

    /// <summary>When set, the goal is soft-deleted (hidden from lists) and can be restored.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
