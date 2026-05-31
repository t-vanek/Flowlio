using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>
/// A recurring spending limit for one expense category (rolled up to include its sub-categories). The
/// <see cref="Amount"/> is in the family's base currency; actual spend is computed per <see cref="Period"/>
/// window from the categorized transactions, FX-converted to the base currency.
/// </summary>
public class Budget : AuditableEntity, ISoftDeletable
{
    public Guid FamilyId { get; set; }

    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>The limit for one period, in the family's base currency.</summary>
    public decimal Amount { get; set; }

    public BudgetPeriod Period { get; set; } = BudgetPeriod.Monthly;

    /// <summary>When set, the budget is soft-deleted (hidden from lists) and can be restored.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
