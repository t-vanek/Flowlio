using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>A bank account belonging to a family. Transactions are imported into it from statements.</summary>
public class BankAccount : AuditableEntity, ISoftDeletable
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

    /// <summary>
    /// The family member who owns this account. When the owner is a <see cref="MemberRole.Child"/>
    /// member this is a child account, controlled by that child's guardian.
    /// </summary>
    public Guid? OwnerMemberId { get; set; }
    public FamilyMember? OwnerMember { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = [];

    /// <summary>Authorized users (disponents) and viewers granted access to this account.</summary>
    public ICollection<AccountAccess> AccessGrants { get; set; } = [];

    /// <summary>Payment cards issued on this account.</summary>
    public ICollection<BankCard> Cards { get; set; } = [];

    /// <summary>When set, the account is archived: hidden from listings and financial views but kept for history.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
