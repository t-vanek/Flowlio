namespace Flowlio.Application.Statements;

/// <summary>A single transaction row extracted from a statement, before it is mapped to a domain entity.</summary>
public sealed record ParsedTransaction
{
    public DateOnly BookingDate { get; init; }
    public DateOnly? ValueDate { get; init; }

    /// <summary>Signed amount: negative for money leaving the account.</summary>
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "CZK";

    public string? CounterpartyName { get; init; }
    public string? CounterpartyAccount { get; init; }
    public string? VariableSymbol { get; init; }
    public string? ConstantSymbol { get; init; }
    public string? SpecificSymbol { get; init; }
    public string? Description { get; init; }
}

/// <summary>Severity of a <see cref="ParseDiagnostic"/> raised while reading or mapping a statement.</summary>
public enum ParseSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>
/// A non-fatal observation made while parsing — e.g. a skipped row or an ambiguous column. Surfaced to
/// the user so silently dropped data becomes visible instead of disappearing.
/// </summary>
public sealed record ParseDiagnostic
{
    public required ParseSeverity Severity { get; init; }
    public required string Message { get; init; }

    /// <summary>1-based source line/row the diagnostic refers to, when known.</summary>
    public int? Line { get; init; }
}

/// <summary>The result of parsing one statement file.</summary>
public sealed record ParsedStatement
{
    /// <summary>Account number detected in the statement header, when the format exposes it.</summary>
    public string? AccountNumber { get; init; }
    public IReadOnlyList<ParsedTransaction> Transactions { get; init; } = [];

    /// <summary>Warnings and skipped-row notices gathered during reading and mapping.</summary>
    public IReadOnlyList<ParseDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Rows that looked like data but could not be mapped (counted from row-level diagnostics).</summary>
    public int SkippedRowCount => Diagnostics.Count(d => d.Line is not null && d.Severity >= ParseSeverity.Warning);
}
