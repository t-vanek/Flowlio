using System.Text.RegularExpressions;
using Flowlio.Application.Statements;

namespace Flowlio.Infrastructure.Statements.Pdf;

/// <summary>
/// Last-resort PDF parser for layouts with no dedicated <see cref="PdfLayout"/>. Pulls out any line that
/// contains a date and an amount and takes the first monetary value as the movement. Crude and lossy — the
/// orchestrator flags its output as experimental — but better than nothing for an unknown bank.
/// </summary>
internal sealed partial class PdfHeuristicParser
{
    public IReadOnlyList<ParsedTransaction> Parse(IReadOnlyList<PdfTextPage> pages)
    {
        var transactions = new List<ParsedTransaction>();

        foreach (var page in pages)
        foreach (var row in page.Rows)
        {
            var line = string.Join(' ', row.Words.Select(w => w.Text));
            var parsed = TryParseLine(line);
            if (parsed is not null)
                transactions.Add(parsed);
        }

        return transactions;
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

        if (!StatementText.TryParseAmount(amountMatches[0].Value, decimalComma: true, out var amount))
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

    [GeneratedRegex(@"[-+]?\d[\d  \.]*,\d{2}")]
    private static partial Regex AmountRegex();
}
