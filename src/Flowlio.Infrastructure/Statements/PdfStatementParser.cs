using System.Text.RegularExpressions;
using Flowlio.Application.Statements;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Flowlio.Infrastructure.Statements;

/// <summary>
/// Heuristic, experimental PDF statement parser. Extracts text in reading order with PdfPig and pulls out
/// lines that contain a date and an amount. PDF layouts vary widely between banks, so this is a best-effort
/// fallback; the importer flags its output as experimental. When the PDF has no extractable text (a scan),
/// OCR would be required.
/// </summary>
internal sealed partial class PdfStatementParser
{
    public ParsedStatement Parse(Stream content, string fileName)
    {
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        ms.Position = 0;

        using var document = PdfDocument.Open(ms);
        var transactions = new List<ParsedTransaction>();

        foreach (var page in document.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page);
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parsed = TryParseLine(line);
                if (parsed is not null)
                    transactions.Add(parsed);
            }
        }

        return new ParsedStatement { Transactions = transactions };
    }

    private static ParsedTransaction? TryParseLine(string line)
    {
        var dateMatch = DateRegex().Match(line);
        if (!dateMatch.Success)
            return null;

        var amountMatches = AmountRegex().Matches(line);
        if (amountMatches.Count == 0)
            return null;

        var bookingDate = StatementText.TryParseDate(dateMatch.Value, ["dd.MM.yyyy", "d.M.yyyy"]);
        if (bookingDate is null)
            return null;

        // The transaction amount is typically the last monetary value on the line (balance excluded
        // when present is hard to distinguish, so we take the first amount as the movement).
        var amountRaw = amountMatches[0].Value;
        if (!StatementText.TryParseAmount(amountRaw, decimalComma: true, out var amount))
            return null;

        var description = line.Replace(dateMatch.Value, string.Empty).Trim();

        return new ParsedTransaction
        {
            BookingDate = bookingDate.Value,
            Amount = amount,
            Currency = "CZK",
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
        };
    }

    [GeneratedRegex(@"\b\d{1,2}\.\s?\d{1,2}\.\s?\d{4}\b")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"[-+]?\d[\d  \.]*,\d{2}")]
    private static partial Regex AmountRegex();
}
