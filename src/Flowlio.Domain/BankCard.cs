using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>
/// A payment card issued on a bank account. Only the last four digits are stored — never the full
/// PAN — so the card can be recognized without holding sensitive data.
/// </summary>
public class BankCard : AuditableEntity
{
    public Guid BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    /// <summary>The member who holds the card (e.g. a spouse or child). Null for an unassigned card.</summary>
    public Guid? HolderMemberId { get; set; }
    public FamilyMember? Holder { get; set; }

    /// <summary>Name embossed on the card.</summary>
    public required string CardholderName { get; set; }

    /// <summary>Last four digits of the card number (display only).</summary>
    public string? Last4 { get; set; }

    public CardType Type { get; set; } = CardType.Debit;

    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }

    public CardStatus Status { get; set; } = CardStatus.Active;

    /// <summary>Optional monthly spending limit, handy for children's cards.</summary>
    public decimal? MonthlyLimit { get; set; }
}
