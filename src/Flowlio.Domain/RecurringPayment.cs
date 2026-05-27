using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>
/// A regular outgoing payment the family expects — rent, loan instalment, utilities, insurance.
/// Matched against imported transactions to confirm it was paid and to forecast cash flow.
/// </summary>
public class RecurringPayment : AuditableEntity
{
    public Guid FamilyId { get; set; }

    public required string Name { get; set; }

    public decimal ExpectedAmount { get; set; }
    public Currency Currency { get; set; } = Currency.CZK;

    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Monthly;

    /// <summary>Day of month (1-31) the payment is due, for monthly+ cadences.</summary>
    public int? DayOfMonth { get; set; }

    public DateOnly? NextDueDate { get; set; }

    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>Account the payment usually leaves from, when known.</summary>
    public Guid? BankAccountId { get; set; }

    /// <summary>Counterparty name used to auto-match imported transactions to this payment.</summary>
    public string? CounterpartyMatch { get; set; }

    /// <summary>Variable symbol used to auto-match imported transactions to this payment.</summary>
    public string? VariableSymbolMatch { get; set; }

    public bool IsActive { get; set; } = true;
}
