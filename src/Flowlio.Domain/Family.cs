using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>A household. The top-level tenant that owns all accounts, transactions and budgets.</summary>
public class Family : AuditableEntity
{
    public required string Name { get; set; }

    /// <summary>Currency the family budgets in. Individual accounts may differ.</summary>
    public Currency BaseCurrency { get; set; } = Currency.CZK;

    public ICollection<FamilyMember> Members { get; set; } = [];
    public ICollection<BankAccount> Accounts { get; set; } = [];
    public ICollection<Category> Categories { get; set; } = [];
}
