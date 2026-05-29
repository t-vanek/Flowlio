using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>
/// User-defined rule that auto-assigns a category to imported transactions whose
/// <see cref="Field"/> contains <see cref="Pattern"/>. Higher <see cref="Priority"/> wins.
/// </summary>
public class CategorizationRule : AuditableEntity, ISoftDeletable
{
    public Guid FamilyId { get; set; }

    /// <summary>Who the rule applies to (personal / single account / whole family). Drives both who may
    /// manage it and which transactions it can categorize.</summary>
    public RuleScope Scope { get; set; } = RuleScope.Family;

    /// <summary>The owning member for a <see cref="RuleScope.Personal"/> rule; null otherwise.</summary>
    public Guid? OwnerMemberId { get; set; }
    public FamilyMember? OwnerMember { get; set; }

    /// <summary>The target account for a <see cref="RuleScope.Account"/> rule; null otherwise.</summary>
    public Guid? BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    public RuleMatchField Field { get; set; } = RuleMatchField.Any;

    /// <summary>How <see cref="Pattern"/> is matched (substring, whole word, or regex).</summary>
    public RuleMatchMode MatchMode { get; set; } = RuleMatchMode.Substring;

    /// <summary>Pattern matched (case- and diacritics-insensitively) against the chosen field,
    /// interpreted according to <see cref="MatchMode"/>. Optional: a rule may match on amount alone.</summary>
    public string? Pattern { get; set; }

    /// <summary>Optional amount condition (with <see cref="MaxAmount"/>): the transaction's absolute amount
    /// must be ≥ this, in <see cref="AmountCurrency"/>. Combined with the text condition by AND.</summary>
    public decimal? MinAmount { get; set; }

    /// <summary>Optional upper bound (inclusive) for the transaction's absolute amount.</summary>
    public decimal? MaxAmount { get; set; }

    /// <summary>Currency the amount bounds are expressed in; required when a bound is set. The amount
    /// condition only applies to transactions in this currency.</summary>
    public string? AmountCurrency { get; set; }

    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }

    public int Priority { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>When set, the rule is soft-deleted (hidden, no longer categorizes) and can be restored.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
