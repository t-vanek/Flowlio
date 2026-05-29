using System.Globalization;

namespace Flowlio.Infrastructure.Exchange;

/// <summary>
/// Parses the Czech National Bank daily FX list ("denní kurz"). The text format is:
/// <code>
/// 06.05.2024 #89
/// země|měna|množství|kód|kurz
/// Austrálie|dolar|1|AUD|16,123
/// EMU|euro|1|EUR|25,045
/// Maďarsko|forint|100|HUF|6,512
/// </code>
/// The first line carries the fixing date, the second is a header, then one currency per line
/// (semicolon-free, pipe-separated, decimal comma). Rates are normalized to CZK per single unit.
/// </summary>
internal static class CnbRateParser
{
    public static (DateOnly Date, IReadOnlyList<(string Code, decimal CzkPerUnit)> Rates) Parse(string content)
    {
        var lines = content.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            throw new FormatException("Unexpected ČNB rate document: too few lines.");

        var date = ParseDate(lines[0]);

        var rates = new List<(string, decimal)>();
        // lines[0] = date, lines[1] = header; data starts at index 2.
        for (var i = 2; i < lines.Length; i++)
        {
            var parts = lines[i].Split('|');
            if (parts.Length < 5)
                continue;

            if (!int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
                continue;
            var code = parts[3].Trim().ToUpperInvariant();
            if (code.Length != 3)
                continue;
            if (!decimal.TryParse(parts[4].Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var quote) || quote <= 0)
                continue;

            rates.Add((code, quote / amount));
        }

        return (date, rates);
    }

    private static DateOnly ParseDate(string headerLine)
    {
        // "06.05.2024 #89" -> take the token before the first space.
        var token = headerLine.Trim().Split(' ', '#')[0];
        return DateOnly.ParseExact(token, "dd.MM.yyyy", CultureInfo.InvariantCulture);
    }
}
