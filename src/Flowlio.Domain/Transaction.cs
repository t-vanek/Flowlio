using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>A single booked movement on a bank account, parsed from an imported statement.</summary>
public class Transaction : AuditableEntity
{
    public Guid FamilyId { get; set; }

    public Guid BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    /// <summary>Date the bank booked the transaction.</summary>
    public DateOnly BookingDate { get; set; }

    /// <summary>Value date (when funds settled); falls back to booking date when absent.</summary>
    public DateOnly? ValueDate { get; set; }

    /// <summary>Signed amount: negative for outgoing, positive for incoming, in <see cref="Currency"/>.</summary>
    public decimal Amount { get; set; }

    public Currency Currency { get; set; } = Currency.CZK;

    public TransactionDirection Direction { get; set; }

    public string? CounterpartyName { get; set; }
    public string? CounterpartyAccount { get; set; }

    public string? VariableSymbol { get; set; }
    public string? ConstantSymbol { get; set; }
    public string? SpecificSymbol { get; set; }

    /// <summary>Free-text description / message for the recipient from the statement.</summary>
    public string? Description { get; set; }

    /// <summary>User note, distinct from the bank-provided description.</summary>
    public string? Note { get; set; }

    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }

    public Guid? ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }

    /// <summary>
    /// Stable fingerprint (account + date + amount + symbols + counterparty) used to detect
    /// duplicates when the same statement period is imported more than once.
    /// </summary>
    public required string DedupHash { get; set; }
}
