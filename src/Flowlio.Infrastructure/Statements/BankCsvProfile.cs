using System.Text;
using Flowlio.Domain;

namespace Flowlio.Infrastructure.Statements;

/// <summary>
/// Describes how a bank's CSV export is laid out. Header names are matched after normalization
/// (lowercase, no diacritics), so several candidate spellings can be listed per logical field.
/// </summary>
public sealed record BankCsvProfile
{
    public required BankProvider Bank { get; init; }

    /// <summary>Field delimiter. When null the parser auto-detects ';' vs ',' vs tab.</summary>
    public char? Delimiter { get; init; }

    public Encoding Encoding { get; init; } = Encoding.UTF8;

    /// <summary>Date formats tried in order. Empty falls back to culture-invariant parsing.</summary>
    public string[] DateFormats { get; init; } = ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd"];

    /// <summary>True when amounts use a comma decimal separator (Czech), false for a dot (Revolut).</summary>
    public bool DecimalComma { get; init; } = true;

    public string[] DateHeaders { get; init; } = [];
    public string[] ValueDateHeaders { get; init; } = [];
    public string[] AmountHeaders { get; init; } = [];
    public string[] CurrencyHeaders { get; init; } = [];
    public string[] CounterpartyNameHeaders { get; init; } = [];
    public string[] CounterpartyAccountHeaders { get; init; } = [];
    public string[] VariableSymbolHeaders { get; init; } = [];
    public string[] ConstantSymbolHeaders { get; init; } = [];
    public string[] SpecificSymbolHeaders { get; init; } = [];
    public string[] DescriptionHeaders { get; init; } = [];
}
