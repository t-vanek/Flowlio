using Flowlio.Application.Statements;

namespace Flowlio.Infrastructure.Statements;

/// <summary>
/// Maps a bank-neutral <see cref="RawStatement"/> to the canonical <see cref="ParsedStatement"/> using a
/// <see cref="BankProfile"/>: resolves columns by candidate header name, applies the amount convention and
/// date/decimal formats, and records a diagnostic for every transaction-shaped row it has to skip.
/// </summary>
internal sealed class StatementMapper
{
    public ParsedStatement Map(RawStatement raw, BankProfile profile)
    {
        var diagnostics = new List<ParseDiagnostic>(raw.Diagnostics);

        var index = BuildHeaderIndex(raw.Headers);
        var dateCol = Resolve(index, profile.Fields.Date);
        var signedCol = Resolve(index, profile.Amount.Signed);
        var debitCol = Resolve(index, profile.Amount.Debit);
        var creditCol = Resolve(index, profile.Amount.Credit);

        if (dateCol < 0)
            diagnostics.Add(Error("Ve výpisu chybí sloupec s datem."));
        if (signedCol < 0 && debitCol < 0 && creditCol < 0)
            diagnostics.Add(Error("Ve výpisu chybí sloupec s částkou."));

        if (dateCol < 0 || (signedCol < 0 && debitCol < 0 && creditCol < 0))
            return new ParsedStatement { AccountNumber = raw.AccountNumber, Diagnostics = diagnostics };

        var valueDateCol = Resolve(index, profile.Fields.ValueDate);
        var currencyCol = Resolve(index, profile.Fields.Currency);
        var nameCol = Resolve(index, profile.Fields.CounterpartyName);
        var accountCol = Resolve(index, profile.Fields.CounterpartyAccount);
        var vsCol = Resolve(index, profile.Fields.VariableSymbol);
        var ksCol = Resolve(index, profile.Fields.ConstantSymbol);
        var ssCol = Resolve(index, profile.Fields.SpecificSymbol);
        var descCol = Resolve(index, profile.Fields.Description);

        var transactions = new List<ParsedTransaction>();
        foreach (var row in raw.Rows)
        {
            var bookingDate = StatementText.TryParseDate(row.Cell(dateCol), profile.DateFormats);
            var hasAmount = TryResolveAmount(row, profile, signedCol, debitCol, creditCol, out var amount);

            if (bookingDate is null)
            {
                // Only flag rows that otherwise look like a transaction; silently ignore footers/totals.
                if (hasAmount)
                    diagnostics.Add(Skip(row.SourceLine, $"nečitelné datum „{row.Cell(dateCol)}“"));
                continue;
            }

            if (!hasAmount)
            {
                diagnostics.Add(Skip(row.SourceLine, "nečitelná nebo chybějící částka"));
                continue;
            }

            transactions.Add(new ParsedTransaction
            {
                BookingDate = bookingDate.Value,
                ValueDate = StatementText.TryParseDate(row.Cell(valueDateCol), profile.DateFormats),
                Amount = amount,
                Currency = NullIfBlank(row.Cell(currencyCol)) ?? "CZK",
                CounterpartyName = NullIfBlank(row.Cell(nameCol)),
                CounterpartyAccount = NullIfBlank(row.Cell(accountCol)),
                VariableSymbol = NullIfBlank(row.Cell(vsCol)),
                ConstantSymbol = NullIfBlank(row.Cell(ksCol)),
                SpecificSymbol = NullIfBlank(row.Cell(ssCol)),
                Description = NullIfBlank(row.Cell(descCol)),
            });
        }

        return new ParsedStatement
        {
            AccountNumber = raw.AccountNumber,
            Transactions = transactions,
            Diagnostics = diagnostics,
        };
    }

    private static bool TryResolveAmount(RawRow row, BankProfile profile, int signedCol, int debitCol, int creditCol, out decimal amount)
    {
        amount = 0m;

        if (signedCol >= 0 && StatementText.TryParseAmount(row.Cell(signedCol), profile.DecimalComma, out var signed))
        {
            amount = signed;
            return true;
        }

        decimal debit = 0m, credit = 0m;
        var hasDebit = debitCol >= 0 && StatementText.TryParseAmount(row.Cell(debitCol), profile.DecimalComma, out debit);
        var hasCredit = creditCol >= 0 && StatementText.TryParseAmount(row.Cell(creditCol), profile.DecimalComma, out credit);

        if (hasDebit || hasCredit)
        {
            // Debit = money leaving the account (negative); credit = money in (positive).
            amount = Math.Abs(credit) - Math.Abs(debit);
            return true;
        }

        return false;
    }

    private static Dictionary<string, int> BuildHeaderIndex(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>();
        for (var i = 0; i < headers.Count; i++)
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

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ParseDiagnostic Error(string message) =>
        new() { Severity = ParseSeverity.Error, Message = message };

    private static ParseDiagnostic Skip(int line, string reason) =>
        new() { Severity = ParseSeverity.Warning, Line = line, Message = $"Přeskočeno: {reason}." };
}
