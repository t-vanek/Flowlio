using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>A bank account belonging to a family. Transactions are imported into it from statements.</summary>
public class BankAccount : AuditableEntity
{
    public Guid FamilyId { get; set; }
    public Family? Family { get; set; }

    public required string Name { get; set; }
    public BankProvider Bank { get; set; } = BankProvider.Other;

    /// <summary>Account number / IBAN as printed on statements. Used to attribute imports.</summary>
    public string? AccountNumber { get; set; }

    public string Currency { get; set; } = "CZK";

    /// <summary>Balance before the first imported transaction; running balances build on this.</summary>
    public decimal OpeningBalance { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = [];
}
