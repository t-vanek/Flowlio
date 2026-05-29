using Flowlio.Infrastructure.Exchange;
using Xunit;

namespace Flowlio.Tests;

public class CnbRateParserTests
{
    private const string Sample =
        "06.05.2024 #89\n" +
        "země|měna|množství|kód|kurz\n" +
        "EMU|euro|1|EUR|25,045\n" +
        "Maďarsko|forint|100|HUF|6,512\n" +
        "USA|dolar|1|USD|23,200\n";

    [Fact]
    public void Parses_date_from_header()
    {
        var (date, _) = CnbRateParser.Parse(Sample);
        Assert.Equal(new DateOnly(2024, 5, 6), date);
    }

    [Fact]
    public void Normalizes_rates_to_czk_per_single_unit()
    {
        var (_, rates) = CnbRateParser.Parse(Sample);

        Assert.Equal(25.045m, RateFor(rates, "EUR"));
        Assert.Equal(23.200m, RateFor(rates, "USD"));
        // 100 HUF = 6,512 CZK -> per unit 0.06512.
        Assert.Equal(0.06512m, RateFor(rates, "HUF"));
    }

    [Fact]
    public void Handles_crlf_and_ignores_malformed_lines()
    {
        var content = "06.05.2024 #89\r\nzemě|měna|množství|kód|kurz\r\nEMU|euro|1|EUR|25,0\r\ngarbage line\r\n";
        var (_, rates) = CnbRateParser.Parse(content);

        Assert.Single(rates);
        Assert.Equal(25.0m, RateFor(rates, "EUR"));
    }

    private static decimal RateFor(IReadOnlyList<(string Code, decimal CzkPerUnit)> rates, string code) =>
        rates.Single(r => r.Code == code).CzkPerUnit;
}
