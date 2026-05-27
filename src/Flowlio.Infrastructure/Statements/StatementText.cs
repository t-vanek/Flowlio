using System.Globalization;
using System.Text;

namespace Flowlio.Infrastructure.Statements;

/// <summary>Helpers for normalizing statement header names and parsing Czech-formatted numbers/dates.</summary>
internal static class StatementText
{
    /// <summary>Lowercases, trims and strips diacritics so "Č&#237;slo &#250;čtu" matches "cislo uctu".</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var formD = value.Trim().Trim('"').Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Parses an amount that may use a comma decimal separator and spaces/NBSP as thousands grouping.</summary>
    public static bool TryParseAmount(string? raw, bool decimalComma, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var cleaned = raw
            .Replace(" ", string.Empty) // non-breaking space
            .Replace(" ", string.Empty)
            .Replace("CZK", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Kč", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (decimalComma)
            cleaned = cleaned.Replace(".", string.Empty).Replace(',', '.');
        else
            cleaned = cleaned.Replace(",", string.Empty);

        return decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out amount);
    }

    public static DateOnly? TryParseDate(string? raw, string[] formats)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        // Some banks append a time component; keep only the date portion.
        var spaceIdx = trimmed.IndexOf(' ');
        if (spaceIdx > 0)
            trimmed = trimmed[..spaceIdx];

        foreach (var fmt in formats)
        {
            if (DateOnly.TryParseExact(trimmed, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d;
        }
        return DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback)
            ? fallback
            : null;
    }
}
