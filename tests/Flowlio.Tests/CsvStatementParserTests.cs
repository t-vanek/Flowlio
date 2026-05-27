using System.Text;
using Flowlio.Application.Statements;
using Flowlio.Infrastructure.Statements;
using Xunit;

namespace Flowlio.Tests;

public class StatementParsingTests
{
    private static ParsedStatement Parse(string csv, string profileId)
    {
        var registry = new BankProfileRegistry();
        var profile = registry.ById(profileId)!;
        var reader = new CsvStatementReader();
        var options = new ReaderOptions
        {
            // Force auto-decoding so the UTF-8 fixtures below are read regardless of the profile's encoding.
            Encoding = null,
            Delimiter = profile.CsvDelimiter,
            KnownHeaderTokens = registry.KnownHeaderTokens,
        };

        var raw = reader.Read(new MemoryStream(Encoding.UTF8.GetBytes(csv)), "vypis.csv", options);
        return new StatementMapper().Map(raw, profile);
    }

    [Fact]
    public void Parses_fio_style_csv_with_comma_decimals_and_quotes()
    {
        const string csv = """
            "Datum";"Objem";"Měna";"Protiúčet";"Název protiúčtu";"VS";"KS";"SS";"Zpráva pro příjemce"
            "01.05.2026";"-1 500,00";"CZK";"123456789/0800";"Albert";"123";"";"";"Nakup potravin"
            "03.05.2026";"45000,00";"CZK";"";"Zamestnavatel";"";"";"";"Mzda"
            """;

        var result = Parse(csv, "fio");

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

        var result = Parse(csv, "fio");

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

        var result = Parse(csv, "revolut");

        Assert.Single(result.Transactions);
        Assert.Equal(new DateOnly(2026, 5, 2), result.Transactions[0].BookingDate);
        Assert.Equal(-149.00m, result.Transactions[0].Amount);
    }

    [Fact]
    public void Combines_separate_debit_and_credit_columns()
    {
        const string csv = """
            Datum;Odepsáno;Připsáno;Měna;Protiúčet
            05.05.2026;1 500,00;;CZK;123456789/0800
            06.05.2026;;45000,00;CZK;
            """;

        var result = Parse(csv, "czech");

        Assert.Equal(2, result.Transactions.Count);
        Assert.Equal(-1500.00m, result.Transactions[0].Amount); // debit -> money out
        Assert.Equal(45000.00m, result.Transactions[1].Amount);  // credit -> money in
    }

    [Fact]
    public void Reports_skipped_rows_as_diagnostics_instead_of_dropping_them_silently()
    {
        const string csv = """
            "Datum";"Objem";"Měna"
            "01.05.2026";"-100,00";"CZK"
            "not-a-date";"-200,00";"CZK"
            """;

        var result = Parse(csv, "fio");

        Assert.Single(result.Transactions);
        Assert.Equal(1, result.SkippedRowCount);

        var warning = Assert.Single(result.Diagnostics, d => d.Severity == ParseSeverity.Warning);
        Assert.NotNull(warning.Line);
        Assert.Contains("datum", warning.Message);
    }
}
