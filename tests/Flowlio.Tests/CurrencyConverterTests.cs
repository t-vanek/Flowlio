using Flowlio.Application.Currency;
using Flowlio.Domain;
using Xunit;

namespace Flowlio.Tests;

public class CurrencyConverterTests
{
    private static ExchangeRate Rate(string currency, string date, decimal czkPerUnit) =>
        new() { Currency = currency, Date = DateOnly.Parse(date), CzkPerUnit = czkPerUnit };

    private static CurrencyConverter Converter() => new(new[]
    {
        Rate("EUR", "2024-05-06", 25.0m),
        Rate("EUR", "2024-05-10", 26.0m),
        Rate("USD", "2024-05-06", 23.0m),
    });

    [Fact]
    public void Czk_is_identity()
    {
        Assert.Equal(1m, Converter().CzkPerUnit("CZK", DateOnly.Parse("2024-01-01")));
    }

    [Fact]
    public void Uses_most_recent_rate_on_or_before_the_date()
    {
        var c = Converter();
        Assert.Equal(25.0m, c.CzkPerUnit("EUR", DateOnly.Parse("2024-05-07"))); // between the two fixings
        Assert.Equal(26.0m, c.CzkPerUnit("EUR", DateOnly.Parse("2024-05-12")));
    }

    [Fact]
    public void Returns_null_before_first_rate_or_for_unknown_currency()
    {
        var c = Converter();
        Assert.Null(c.CzkPerUnit("EUR", DateOnly.Parse("2024-05-01")));
        Assert.Null(c.CzkPerUnit("GBP", DateOnly.Parse("2024-05-06")));
    }

    [Fact]
    public void Converts_foreign_to_czk_and_back()
    {
        var c = Converter();
        var date = DateOnly.Parse("2024-05-06");
        Assert.Equal(2500m, c.Convert(100m, "EUR", "CZK", date));
        Assert.Equal(100m, c.Convert(2500m, "CZK", "EUR", date));
    }

    [Fact]
    public void Converts_between_two_foreign_currencies_via_czk()
    {
        var c = Converter();
        var date = DateOnly.Parse("2024-05-06");
        // 23 USD = 23*23 = 529 CZK = 529/25 EUR.
        Assert.Equal(529m / 25m, c.Convert(23m, "USD", "EUR", date));
    }

    [Fact]
    public void Same_currency_returns_the_amount_even_without_a_rate()
    {
        Assert.Equal(42m, Converter().Convert(42m, "GBP", "GBP", DateOnly.Parse("2024-05-06")));
    }

    [Fact]
    public void Returns_null_when_a_required_rate_is_missing()
    {
        var c = Converter();
        Assert.Null(c.Convert(100m, "EUR", "GBP", DateOnly.Parse("2024-05-06"))); // GBP unknown
        Assert.Null(c.Convert(100m, "EUR", "CZK", DateOnly.Parse("2024-05-01"))); // before first EUR fixing
    }
}
