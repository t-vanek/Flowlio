using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>A household. The top-level tenant that owns all accounts, transactions and budgets.</summary>
public class Family : AuditableEntity
{
    public required string Name { get; set; }

    /// <summary>ISO 4217 currency the family budgets in (e.g. CZK). Accounts may differ.</summary>
    public string BaseCurrency { get; set; } = "CZK";

    public ICollection<FamilyMember> Members { get; set; } = [];
    public ICollection<BankAccount> Accounts { get; set; } = [];
    public ICollection<Category> Categories { get; set; } = [];
}
