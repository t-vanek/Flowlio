using Flowlio.Domain;

namespace Flowlio.Infrastructure.Statements.Pdf;

/// <summary>Logical meaning of a column in a bank's PDF statement table.</summary>
internal enum PdfField
{
    Ignore = 0,
    Date,
    Description,
    CounterpartyName,
    CounterpartyAccount,
    Details,
    Amount,
    Balance,
    Fees,
    Identification,
}

/// <summary>A column anchored at a horizontal position (the X of its header label, in PDF points).</summary>
internal sealed record PdfColumn(PdfField Field, double X);

/// <summary>
/// Data-driven description of one bank's PDF statement layout. The table parser reconstructs columns from
/// the <see cref="Columns"/> X-anchors, anchors each transaction on the row carrying an amount, and folds
/// continuation rows in. Per-bank quirks are expressed as flags rather than code branches so adding a bank
/// is (mostly) a matter of adding a layout.
/// </summary>
internal sealed record PdfLayout
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required BankProvider Bank { get; init; }

    public required IReadOnlyList<PdfColumn> Columns { get; init; }

    /// <summary>Substrings that, found anywhere in the document text, identify this bank (BIC, header tokens).</summary>
    public required IReadOnlyList<string> DetectionMarkers { get; init; }

    public string[] DateFormats { get; init; } = ["dd.MM.yyyy", "d.M.yyyy"];

    /// <summary>False when the date column omits the year (ČSOB "02.07."), inferred from the statement period.</summary>
    public bool DateHasYear { get; init; } = true;

    public bool DecimalComma { get; init; } = true;

    /// <summary>How far left of the next column's anchor the boundary sits; keeps left-aligned text out of
    /// right-aligned numeric columns (amount/balance). In PDF points.</summary>
    public double ColumnPad { get; init; } = 22;

    /// <summary>Columns whose values build the transaction description, in order.</summary>
    public PdfField[] DescriptionFields { get; init; } = [PdfField.Description];

    /// <summary>Separator used when joining several description fields.</summary>
    public string DescriptionSeparator { get; init; } = " ";

    /// <summary>Column on continuation rows that holds the counterparty account number.</summary>
    public PdfField AccountSourceField { get; init; } = PdfField.CounterpartyAccount;

    /// <summary>When set, VS/KS/SS are pulled from free text (Details) via regex rather than dedicated columns.</summary>
    public bool SymbolsInline { get; init; }

    /// <summary>When set and the description marks a card payment, the counterparty name is taken from the
    /// Details (the merchant) instead of the Name column (which holds the cardholder).</summary>
    public bool CardCounterpartyFromDetails { get; init; }

    /// <summary>Marker (in the description) identifying a card payment, for <see cref="CardCounterpartyFromDetails"/>.</summary>
    public string CardPaymentMarker { get; init; } = "kartou";

    /// <summary>When set, a card-payment description embeds the merchant after this marker (ČSOB "Místo:"). The
    /// merchant becomes the counterparty, the noisy tail after <see cref="CardAmountMarker"/> is dropped from the
    /// description, and the date in that tail (the purchase date) becomes the value date.</summary>
    public string? CardMerchantMarker { get; init; }

    /// <summary>Marker that ends the merchant text and begins the amount/date tail (ČSOB "Částka:").</summary>
    public string CardAmountMarker { get; init; } = "Částka:";

    /// <summary>Whether a continuation row may legitimately carry a date in the date column (Air Bank's value
    /// date sits there). False for layouts (ČSOB) where a dated row is always a new entry — e.g. an
    /// interest-rate-change notice — so it must end the current block rather than be folded into it.</summary>
    public bool ContinuationMayHaveDate { get; init; } = true;
}
