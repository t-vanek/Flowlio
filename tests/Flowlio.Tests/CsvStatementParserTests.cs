using System.Text;
using Flowlio.Application.Statements;
using Flowlio.Domain;
using Flowlio.Infrastructure.Statements;
using Xunit;

namespace Flowlio.Tests;

public class CsvStatementParserTests
{
    private static Stream ToStream(string text, Encoding encoding) => new MemoryStream(encoding.GetBytes(text));

    [Fact]
    public void Parses_fio_style_csv_with_comma_decimals_and_quotes()
    {
        const string csv = """
            "Datum";"Objem";"Měna";"Protiúčet";"Název protiúčtu";"VS";"KS";"SS";"Zpráva pro příjemce"
            "01.05.2026";"-1 500,00";"CZK";"123456789/0800";"Albert";"123";"";"";"Nakup potravin"
            "03.05.2026";"45000,00";"CZK";"";"Zamestnavatel";"";"";"";"Mzda"
            """;

        var parser = new CsvStatementParser(BankCsvProfiles.Fio);
        var result = parser.Parse(ToStream(csv, Encoding.UTF8), "vypis.csv");

        Assert.Equal(2, result.Transactions.Count);

        var first = result.Transactions[0];
        Assert.Equal(new DateOnly(2026, 5, 1), first.BookingDate);
        Assert.Equal(-1500.00m, first.Amount);
        Assert.Equal("CZK", first.Currency);
        Assert.Equal("Albert", first.CounterpartyName);
        Assert.Equal("123", first.VariableSymbol);

        Assert.Equal(45000.00m, result.Transactions[1].Amount);
    }

    [Fact]
    public void Skips_header_preamble_lines()
    {
        const string csv = """
            Výpis z účtu 123456789/2010
            Období: 01.05.2026 - 31.05.2026

            "Datum";"Objem";"Měna";"Název protiúčtu"
            "10.05.2026";"-299,00";"CZK";"Netflix"
            """;

        var parser = new CsvStatementParser(BankCsvProfiles.Fio);
        var result = parser.Parse(ToStream(csv, Encoding.UTF8), "vypis.csv");

        Assert.Single(result.Transactions);
        Assert.Equal(-299.00m, result.Transactions[0].Amount);
    }

    [Fact]
    public void Parses_revolut_csv_with_dot_decimals()
    {
        const string csv = """
            Type,Product,Started Date,Completed Date,Description,Amount,Currency,State,Balance
            CARD_PAYMENT,Current,2026-05-02 10:00:00,2026-05-02 10:01:00,Spotify,-149.00,CZK,COMPLETED,5000.00
            """;

        var parser = new CsvStatementParser(BankCsvProfiles.Revolut);
        var result = parser.Parse(ToStream(csv, Encoding.UTF8), "revolut.csv");

        Assert.Single(result.Transactions);
        Assert.Equal(new DateOnly(2026, 5, 2), result.Transactions[0].BookingDate);
        Assert.Equal(-149.00m, result.Transactions[0].Amount);
    }
}
