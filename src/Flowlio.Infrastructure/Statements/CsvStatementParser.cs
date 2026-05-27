using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Flowlio.Application.Statements;
using Flowlio.Domain;

namespace Flowlio.Infrastructure.Statements;

/// <summary>
/// Generic CSV statement parser driven by a <see cref="BankCsvProfile"/>. Skips any header preamble
/// (account metadata lines some banks prepend), auto-detects the delimiter when unspecified, and maps
/// columns by normalized header name.
/// </summary>
public sealed class CsvStatementParser(BankCsvProfile profile) : IStatementParser
{
    public BankProvider Bank => profile.Bank;
    public ImportFormat Format => ImportFormat.Csv;

    public ParsedStatement Parse(Stream content, string fileName)
    {
        using var reader = new StreamReader(content, profile.Encoding, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        var (headerLineIndex, delimiter) = LocateHeader(lines);
        if (headerLineIndex < 0)
            return new ParsedStatement();

        var tableText = string.Join('\n', lines[headerLineIndex..]);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            HasHeaderRecord = true,
            DetectColumnCountChanges = false,
            HeaderValidated = null,
            MissingFieldFound = null,
            BadDataFound = null,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
        };

        using var csv = new CsvReader(new StringReader(tableText), config);
        if (!csv.Read() || !csv.ReadHeader())
            return new ParsedStatement();

        var headers = csv.HeaderRecord ?? [];
        var index = BuildHeaderIndex(headers);

        var dateCol = Resolve(index, profile.DateHeaders);
        var amountCol = Resolve(index, profile.AmountHeaders);
        if (dateCol < 0 || amountCol < 0)
            return new ParsedStatement();

        var valueDateCol = Resolve(index, profile.ValueDateHeaders);
        var currencyCol = Resolve(index, profile.CurrencyHeaders);
        var nameCol = Resolve(index, profile.CounterpartyNameHeaders);
        var accountCol = Resolve(index, profile.CounterpartyAccountHeaders);
        var vsCol = Resolve(index, profile.VariableSymbolHeaders);
        var ksCol = Resolve(index, profile.ConstantSymbolHeaders);
        var ssCol = Resolve(index, profile.SpecificSymbolHeaders);
        var descCol = Resolve(index, profile.DescriptionHeaders);

        var transactions = new List<ParsedTransaction>();
        while (csv.Read())
        {
            var dateRaw = Field(csv, dateCol);
            var bookingDate = StatementText.TryParseDate(dateRaw, profile.DateFormats);
            if (bookingDate is null)
                continue; // skip non-data rows (totals, footers)

            if (!StatementText.TryParseAmount(Field(csv, amountCol), profile.DecimalComma, out var amount))
                continue;

            transactions.Add(new ParsedTransaction
            {
                BookingDate = bookingDate.Value,
                ValueDate = StatementText.TryParseDate(Field(csv, valueDateCol), profile.DateFormats),
                Amount = amount,
                Currency = NullIfBlank(Field(csv, currencyCol)) ?? "CZK",
                CounterpartyName = NullIfBlank(Field(csv, nameCol)),
                CounterpartyAccount = NullIfBlank(Field(csv, accountCol)),
                VariableSymbol = NullIfBlank(Field(csv, vsCol)),
                ConstantSymbol = NullIfBlank(Field(csv, ksCol)),
                SpecificSymbol = NullIfBlank(Field(csv, ssCol)),
                Description = NullIfBlank(Field(csv, descCol)),
            });
        }

        return new ParsedStatement { Transactions = transactions };
    }

    /// <summary>Finds the header row and the delimiter, scanning past any leading metadata lines.</summary>
    private (int Line, char Delimiter) LocateHeader(string[] lines)
    {
        char[] candidates = profile.Delimiter is { } d ? [d] : [';', ',', '\t'];

        for (var i = 0; i < lines.Length; i++)
        {
            foreach (var delim in candidates)
            {
                var cells = lines[i].Split(delim);
                if (cells.Length < 2)
                    continue;

                var normalized = cells.Select(StatementText.Normalize).ToArray();
                var hasDate = profile.DateHeaders.Any(h => normalized.Contains(StatementText.Normalize(h)));
                var hasAmount = profile.AmountHeaders.Any(h => normalized.Contains(StatementText.Normalize(h)));
                if (hasDate && hasAmount)
                    return (i, delim);
            }
        }
        return (-1, ';');
    }

    private static Dictionary<string, int> BuildHeaderIndex(string[] headers)
    {
        var map = new Dictionary<string, int>();
        for (var i = 0; i < headers.Length; i++)
        {
            var key = StatementText.Normalize(headers[i]);
            if (key.Length > 0 && !map.ContainsKey(key))
                map[key] = i;
        }
        return map;
    }

    private static int Resolve(Dictionary<string, int> index, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (index.TryGetValue(StatementText.Normalize(candidate), out var i))
                return i;
        }
        return -1;
    }

    private static string? Field(CsvReader csv, int col) =>
        col >= 0 ? csv.GetField(col) : null;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
