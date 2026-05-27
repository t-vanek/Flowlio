using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>A paid service subscription (streaming, software, memberships) the family tracks separately.</summary>
public class Subscription : AuditableEntity
{
    public Guid FamilyId { get; set; }

    public required string Name { get; set; }

    /// <summary>Provider / merchant, e.g. "Netflix", used to match statement entries.</summary>
    public string? Provider { get; set; }

    public decimal Amount { get; set; }
    public Currency Currency { get; set; } = Currency.CZK;

    public RecurrenceFrequency BillingCycle { get; set; } = RecurrenceFrequency.Monthly;

    public DateOnly? NextRenewalDate { get; set; }

    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Optional link or note on how to cancel, kept handy for the family.</summary>
    public string? Notes { get; set; }
}
