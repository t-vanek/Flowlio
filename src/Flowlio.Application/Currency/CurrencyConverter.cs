using Flowlio.Domain;

namespace Flowlio.Application.Currency;

/// <summary>
/// Converts amounts between currencies using a snapshot of ČNB rates (CZK as the pivot). For a given date it
/// uses the most recent rate on or before that date (ČNB doesn't publish on weekends/holidays). CZK is
/// implicitly 1. Returns null when a required rate is unavailable, so callers can surface "rate missing"
/// instead of silently assuming 1:1.
/// </summary>
public sealed class CurrencyConverter
{
    private const string Czk = "CZK";
    private readonly Dictionary<string, (DateOnly Date, decimal CzkPerUnit)[]> _byCurrency;

    public CurrencyConverter(IEnumerable<ExchangeRate> rates)
    {
        _byCurrency = rates
            .GroupBy(r => r.Currency.ToUpperInvariant())
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.Date).Select(r => (r.Date, r.CzkPerUnit)).ToArray());
    }

    /// <summary>CZK for one unit of <paramref name="currency"/> on or before <paramref name="date"/>;
    /// 1 for CZK, null when no rate is known.</summary>
    public decimal? CzkPerUnit(string currency, DateOnly date)
    {
        if (string.Equals(currency, Czk, StringComparison.OrdinalIgnoreCase))
            return 1m;
        if (!_byCurrency.TryGetValue(currency.ToUpperInvariant(), out var series))
            return null;

        decimal? rate = null;
        foreach (var point in series)
        {
            if (point.Date <= date)
                rate = point.CzkPerUnit;
            else
                break; // series is ascending by date
        }
        return rate;
    }

    /// <summary>Converts <paramref name="amount"/> from one currency to another using the rates as of
    /// <paramref name="date"/>. Returns null if either side's rate is missing.</summary>
    public decimal? Convert(decimal amount, string from, string to, DateOnly date)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return amount;
        if (CzkPerUnit(from, date) is not { } fromCzk || CzkPerUnit(to, date) is not { } toCzk || toCzk == 0)
            return null;
        return amount * fromCzk / toCzk;
    }
}
