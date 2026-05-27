using System.Text;

namespace Flowlio.Infrastructure.Statements;

/// <summary>
/// Data-driven description of one bank's tabular export. Header names are matched after normalization
/// (lowercase, no diacritics), so several candidate spellings can be listed per logical field. Adding a
/// bank means adding a profile to <see cref="BankProfileRegistry"/> — no parser code or switch changes.
/// </summary>
internal sealed record BankProfile
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Forced delimiter for CSV; null lets the reader auto-detect ';' vs ',' vs tab.</summary>
    public char? CsvDelimiter { get; init; }

    /// <summary>Forced encoding for CSV; null lets the reader detect it (BOM / UTF-8 / Windows-1250).</summary>
    public Encoding? Encoding { get; init; }

    public string[] DateFormats { get; init; } = ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd"];

    /// <summary>True when amounts use a comma decimal separator (Czech), false for a dot (Revolut).</summary>
    public bool DecimalComma { get; init; } = true;

    public required FieldMap Fields { get; init; }
    public AmountConvention Amount { get; init; } = new();
    public StatementFingerprint Fingerprint { get; init; } = new();
}

/// <summary>Candidate header spellings for each logical field other than the amount.</summary>
internal sealed record FieldMap
{
    public string[] Date { get; init; } = [];
    public string[] ValueDate { get; init; } = [];
    public string[] Currency { get; init; } = [];
    public string[] CounterpartyName { get; init; } = [];
    public string[] CounterpartyAccount { get; init; } = [];
    public string[] VariableSymbol { get; init; } = [];
    public string[] ConstantSymbol { get; init; } = [];
    public string[] SpecificSymbol { get; init; } = [];
    public string[] Description { get; init; } = [];

    public IEnumerable<string> AllHeaders =>
        Date.Concat(ValueDate).Concat(Currency).Concat(CounterpartyName).Concat(CounterpartyAccount)
            .Concat(VariableSymbol).Concat(ConstantSymbol).Concat(SpecificSymbol).Concat(Description);
}

/// <summary>
/// How a bank encodes the transaction amount. A single signed column is preferred when present; otherwise
/// separate debit/credit magnitude columns are combined (credit − debit), covering KB/ČS-style exports.
/// </summary>
internal sealed record AmountConvention
{
    public string[] Signed { get; init; } = [];
    public string[] Debit { get; init; } = [];
    public string[] Credit { get; init; } = [];

    public IEnumerable<string> AllHeaders => Signed.Concat(Debit).Concat(Credit);
}

/// <summary>Header tokens that, when all present, identify a bank's export during auto-detection.</summary>
internal sealed record StatementFingerprint
{
    public string[] RequiredHeaders { get; init; } = [];
}
