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

/// <summary>The result of parsing one statement file.</summary>
public sealed record ParsedStatement
{
    /// <summary>Account number detected in the statement header, when the format exposes it.</summary>
    public string? AccountNumber { get; init; }
    public IReadOnlyList<ParsedTransaction> Transactions { get; init; } = [];
}
