using System.Text.RegularExpressions;

namespace Flowlio.Infrastructure.Statements;

/// <summary>
/// Best-effort extraction of a human merchant name from a card-payment description, used when a statement
/// row carries no dedicated counterparty (typical for card payments — the merchant lives in the description).
/// Conservative on purpose: only fires for rows that look like card payments and returns null unless something
/// name-like survives stripping markers, dates, card masks and amounts. Categorization does not depend on this
/// (rules can match the raw description); it mainly yields a clean counterparty for display and counterparty rules.
/// </summary>
public static partial class MerchantName
{
    public static string? FromDescription(string? description, string? details = null)
    {
        var text = string.Join(' ',
            new[] { description, details }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Only synthesize a merchant for card-like rows, so transfers/fees keep an empty counterparty.
        if (!CardMarkerRegex().IsMatch(text) && !MaskedCardRegex().IsMatch(text))
            return null;

        text = CardMarkerRegex().Replace(text, " ");
        text = MaskedCardRegex().Replace(text, " ");
        text = DateRegex().Replace(text, " ");
        text = MoneyRegex().Replace(text, " ");
        text = CurrencyCodeRegex().Replace(text, " ");
        text = LongNumberRegex().Replace(text, " ");
        text = WhitespaceRegex().Replace(text, " ").Trim(' ', '-', '·', ',', ';', ':');

        // Require a couple of characters with at least one letter, so we never return leftover punctuation/digits.
        return text.Length >= 2 && text.Any(char.IsLetter) ? text : null;
    }

    // "platba kartou", "nákup kartou", "úhrada kartou", bare "kartou", and English "card payment"/"purchase".
    [GeneratedRegex(@"(platba|nákup|nakup|úhrada|uhrada|bezhotovostní|bezhotovostni|výběr|vyber)?\s*kartou|card\s+payment|purchase",
        RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex CardMarkerRegex();

    // Whitespace-delimited token containing a masking star, e.g. "123456******1234" or "****1234".
    [GeneratedRegex(@"\S*\*+\S*")]
    private static partial Regex MaskedCardRegex();

    // dd.mm. or dd.mm.yyyy (statement dates embedded in the label).
    [GeneratedRegex(@"\b\d{1,2}\.\d{1,2}\.(\d{2,4})?")]
    private static partial Regex DateRegex();

    // Money amounts like "250,00", "1 250.00", "1.250,50".
    [GeneratedRegex(@"\b\d{1,3}(?:[ .]\d{3})*[.,]\d{2}\b")]
    private static partial Regex MoneyRegex();

    [GeneratedRegex(@"\b(czk|eur|usd|gbp|pln|huf|chf)\b", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex CurrencyCodeRegex();

    // Bare long numeric codes (authorization/terminal ids); short numbers in a name are kept.
    [GeneratedRegex(@"\b\d{4,}\b")]
    private static partial Regex LongNumberRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespaceRegex();
}
