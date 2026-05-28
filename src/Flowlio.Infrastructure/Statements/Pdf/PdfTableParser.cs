using System.Text.RegularExpressions;
using Flowlio.Application.Statements;

namespace Flowlio.Infrastructure.Statements.Pdf;

/// <summary>
/// Turns a positioned <see cref="PdfTextPage"/> model into a canonical <see cref="ParsedStatement"/> using a
/// <see cref="PdfLayout"/>. Pure logic (no PdfPig), so it is unit-tested with hand-built fixtures.
///
/// The approach: reconstruct columns from each row's word X-positions; treat a row carrying a parseable
/// amount as a transaction anchor; fold the following continuation rows (account, value date, multi-line
/// details) into it; stop at the statement footer. Dates without a year (ČSOB) get one inferred from the
/// statement period.
/// </summary>
internal sealed partial class PdfTableParser
{
    public ParsedStatement Parse(IReadOnlyList<PdfTextPage> pages, PdfLayout layout)
    {
        var (periodStart, periodEnd) = DetectPeriod(pages);
        var diagnostics = new List<ParseDiagnostic>();
        var transactions = new List<ParsedTransaction>();

        var rowIndex = 0;
        foreach (var block in GroupIntoBlocks(pages, layout))
        {
            rowIndex++;
            var anchor = block[0];

            var amountRaw = Cell(anchor, PdfField.Amount);
            if (!StatementText.TryParseAmount(amountRaw, layout.DecimalComma, out var amount))
            {
                diagnostics.Add(Skip(rowIndex, $"nečitelná částka „{amountRaw}“"));
                continue;
            }

            var booking = ResolveDate(BlockDates(block).FirstOrDefault(), layout, periodStart, periodEnd);
            if (booking is null)
            {
                diagnostics.Add(Skip(rowIndex, $"nečitelné datum „{DateCell(anchor)}“"));
                continue;
            }

            var value = ResolveDate(BlockDates(block).Skip(1).FirstOrDefault(), layout, periodStart, periodEnd);

            var description = BuildDescription(block, layout);
            var details = JoinBlock(block, PdfField.Details);
            var name = ResolveCounterpartyName(anchor, layout, description, details);
            var account = ResolveAccount(block, layout);
            var (vs, ks, ss) = ResolveSymbols(details, layout);

            transactions.Add(new ParsedTransaction
            {
                BookingDate = booking.Value,
                ValueDate = value,
                Amount = amount,
                Currency = "CZK",
                CounterpartyName = NullIfBlank(name),
                CounterpartyAccount = NullIfBlank(account),
                VariableSymbol = vs,
                ConstantSymbol = ks,
                SpecificSymbol = ss,
                Description = NullIfBlank(description),
            });
        }

        if (transactions.Count == 0 && diagnostics.Count == 0)
            diagnostics.Add(new ParseDiagnostic
            {
                Severity = ParseSeverity.Error,
                Message = "V PDF se nepodařilo rozpoznat žádné transakce.",
            });

        return new ParsedStatement { Transactions = transactions, Diagnostics = diagnostics };
    }

    // ---- row grouping ------------------------------------------------------------------------------

    /// <summary>Walks rows top-to-bottom, starting a new block on each amount-bearing row and folding
    /// continuation rows (empty date or a value date) in. A non-date value in the date column — footer prose
    /// or a repeated header — ends the current block.</summary>
    private static List<List<Dictionary<PdfField, string>>> GroupIntoBlocks(
        IReadOnlyList<PdfTextPage> pages, PdfLayout layout)
    {
        var blocks = new List<List<Dictionary<PdfField, string>>>();
        List<Dictionary<PdfField, string>>? current = null;

        foreach (var page in pages)
        foreach (var row in page.Rows)
        {
            var cells = SplitRow(row, layout);
            var dateCell = Cell(cells, PdfField.Date);

            if (IsAmount(Cell(cells, PdfField.Amount), layout))
            {
                current = [cells];
                blocks.Add(current);
            }
            else if (current is not null)
            {
                if (dateCell.Length == 0 || IsDateText(dateCell))
                    current.Add(cells);
                else
                    current = null;
            }
        }

        return blocks;
    }

    /// <summary>Assigns each word to a column. The boundary between adjacent columns sits a pad to the left of
    /// the next anchor (clamped past this one), keeping left-aligned text out of right-aligned numeric columns.</summary>
    private static Dictionary<PdfField, string> SplitRow(PdfTextRow row, PdfLayout layout)
    {
        var cols = layout.Columns;
        var buckets = new Dictionary<PdfField, List<string>>();

        foreach (var word in row.Words)
        {
            var col = cols.Count - 1;
            for (var i = 0; i < cols.Count - 1; i++)
            {
                var boundary = Math.Max(cols[i].X, cols[i + 1].X - layout.ColumnPad);
                if (word.Left < boundary)
                {
                    col = i;
                    break;
                }
            }

            var field = cols[col].Field;
            if (field == PdfField.Ignore)
                continue;
            (buckets.TryGetValue(field, out var list) ? list : buckets[field] = []).Add(word.Text);
        }

        return buckets.ToDictionary(kv => kv.Key, kv => string.Join(' ', kv.Value));
    }

    // ---- field extraction --------------------------------------------------------------------------

    /// <summary>Description = the anchor's label plus any non-noise continuation text in the same column
    /// (e.g. a wrapped type), excluding numeric transaction codes and account numbers.</summary>
    private static string BuildDescription(List<Dictionary<PdfField, string>> block, PdfLayout layout)
    {
        var parts = new List<string>();

        foreach (var field in layout.DescriptionFields)
        {
            if (field == PdfField.Details)
            {
                var details = JoinBlock(block, PdfField.Details);
                if (details.Length > 0)
                    parts.Add(details);
                continue;
            }

            // Description-type column: anchor value, then continuation cells that aren't noise.
            var pieces = new List<string>();
            for (var i = 0; i < block.Count; i++)
            {
                var v = Cell(block[i], field).Trim();
                if (v.Length == 0)
                    continue;
                if (i > 0 && IsNoise(v))
                    continue;
                pieces.Add(v);
            }
            if (pieces.Count > 0)
                parts.Add(string.Join(' ', pieces));
        }

        return string.Join(layout.DescriptionSeparator, parts).Trim();
    }

    private static string ResolveCounterpartyName(
        Dictionary<PdfField, string> anchor, PdfLayout layout, string description, string details)
    {
        if (layout.CardCounterpartyFromDetails
            && description.Contains(layout.CardPaymentMarker, StringComparison.OrdinalIgnoreCase))
        {
            var merchant = Cell(anchor, PdfField.Details).Trim();
            if (merchant.Length > 0)
                return merchant;
        }

        return Cell(anchor, PdfField.CounterpartyName).Trim();
    }

    /// <summary>An account-shaped token on a continuation row (masked card numbers, with '*', are excluded).</summary>
    private static string? ResolveAccount(List<Dictionary<PdfField, string>> block, PdfLayout layout)
    {
        foreach (var row in block.Skip(1))
        {
            var v = Cell(row, layout.AccountSourceField).Trim();
            if (v.Length > 0 && AccountRegex().IsMatch(v.Replace(" ", string.Empty)))
                return v;
        }
        return null;
    }

    private static (string? Vs, string? Ks, string? Ss) ResolveSymbols(string details, PdfLayout layout)
    {
        if (!layout.SymbolsInline || details.Length == 0)
            return (null, null, null);

        return (Match(VsRegex(), details), Match(KsRegex(), details), Match(SsRegex(), details));
    }

    // ---- dates -------------------------------------------------------------------------------------

    private static IEnumerable<string> BlockDates(List<Dictionary<PdfField, string>> block) =>
        block.Select(DateCell).Where(d => d.Length > 0 && IsDateText(d));

    private static DateOnly? ResolveDate(string? raw, PdfLayout layout, DateOnly? periodStart, DateOnly? periodEnd)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (layout.DateHasYear)
            return StatementText.TryParseDate(raw, layout.DateFormats);

        var m = DayMonthRegex().Match(raw.Trim());
        if (!m.Success)
            return null;

        var day = int.Parse(m.Groups[1].Value);
        var month = int.Parse(m.Groups[2].Value);
        return InferYear(day, month, periodStart, periodEnd);
    }

    /// <summary>Picks the year for a year-less date so it falls within the statement period (handles a period
    /// that straddles a year boundary).</summary>
    private static DateOnly? InferYear(int day, int month, DateOnly? start, DateOnly? end)
    {
        if (start is null || end is null)
            return TryDate(DateTime.Now.Year, month, day);

        var candidate = TryDate(start.Value.Year, month, day);
        if (start.Value.Year == end.Value.Year)
            return candidate;

        if (candidate is { } c && c >= start.Value && c <= end.Value)
            return candidate;

        return TryDate(end.Value.Year, month, day);
    }

    private static DateOnly? TryDate(int year, int month, int day)
    {
        try { return new DateOnly(year, month, day); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static (DateOnly? Start, DateOnly? End) DetectPeriod(IReadOnlyList<PdfTextPage> pages)
    {
        var text = string.Join(
            ' ',
            pages.SelectMany(p => p.Rows).SelectMany(r => r.Words).Select(w => w.Text));

        var m = PeriodRegex().Match(text);
        if (!m.Success)
            return (null, null);

        var start = TryDate(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value));
        var end = TryDate(int.Parse(m.Groups[6].Value), int.Parse(m.Groups[5].Value), int.Parse(m.Groups[4].Value));
        return (start, end);
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private static bool IsAmount(string cell, PdfLayout layout)
    {
        var trimmed = cell.Trim();
        if (trimmed.Length == 0)
            return false;
        if (layout.DecimalComma && !trimmed.Contains(','))
            return false;
        return StatementText.TryParseAmount(trimmed, layout.DecimalComma, out _);
    }

    private static bool IsNoise(string value) =>
        NumericRegex().IsMatch(value.Replace(" ", string.Empty))
        || AccountRegex().IsMatch(value.Replace(" ", string.Empty));

    private static string Cell(Dictionary<PdfField, string> cells, PdfField field) =>
        cells.GetValueOrDefault(field, string.Empty);

    private static string DateCell(Dictionary<PdfField, string> cells) => Cell(cells, PdfField.Date).Trim();

    private static string JoinBlock(List<Dictionary<PdfField, string>> block, PdfField field) =>
        string.Join(' ', block.Select(b => Cell(b, field).Trim()).Where(s => s.Length > 0));

    private static bool IsDateText(string text) =>
        FullDateRegex().IsMatch(text.Trim()) || DayMonthRegex().IsMatch(text.Trim());

    private static string? Match(Regex regex, string text)
    {
        var m = regex.Match(text);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ParseDiagnostic Skip(int line, string reason) =>
        new() { Severity = ParseSeverity.Warning, Line = line, Message = $"Přeskočeno: {reason}." };

    [GeneratedRegex(@"^(\d{1,2})\.(\d{1,2})\.(\d{4})$")]
    private static partial Regex FullDateRegex();

    [GeneratedRegex(@"^(\d{1,2})\.(\d{1,2})\.?$")]
    private static partial Regex DayMonthRegex();

    [GeneratedRegex(@"(\d{1,2})\.\s*(\d{1,2})\.\s*(\d{4})\s*-\s*(\d{1,2})\.\s*(\d{1,2})\.\s*(\d{4})")]
    private static partial Regex PeriodRegex();

    [GeneratedRegex(@"^[-+]?\d+([.,]\d+)?$")]
    private static partial Regex NumericRegex();

    [GeneratedRegex(@"^\d{1,6}-?\d{2,16}/?\d{0,4}$")]
    private static partial Regex AccountRegex();

    [GeneratedRegex(@"VS\s*:?\s*(\d{1,10})", RegexOptions.IgnoreCase)]
    private static partial Regex VsRegex();

    [GeneratedRegex(@"KS\s*:?\s*(\d{1,10})", RegexOptions.IgnoreCase)]
    private static partial Regex KsRegex();

    [GeneratedRegex(@"SS\s*:?\s*(\d{1,10})", RegexOptions.IgnoreCase)]
    private static partial Regex SsRegex();
}
