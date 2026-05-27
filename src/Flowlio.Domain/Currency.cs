namespace Flowlio.Domain;

/// <summary>Supported currencies. Stored as the ISO 4217 code (the enum name) in the database.</summary>
public enum Currency
{
    CZK = 0,
    EUR = 1,
    USD = 2,
    GBP = 3,
    PLN = 4,
    CHF = 5,
    HUF = 6,
}

public static class CurrencyExtensions
{
    /// <summary>Parses an ISO code from a statement; falls back to <paramref name="fallback"/> when unknown.</summary>
    public static Currency ParseOrDefault(string? code, Currency fallback = Currency.CZK) =>
        Enum.TryParse<Currency>(code?.Trim(), ignoreCase: true, out var currency) ? currency : fallback;
}
