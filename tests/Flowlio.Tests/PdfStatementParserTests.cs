using Flowlio.Domain;
using Flowlio.Infrastructure.Statements.Pdf;
using Xunit;

namespace Flowlio.Tests;

/// <summary>
/// Coordinate-based PDF parsing tests. Fixtures are hand-built positioned-text pages (the seam produced by the
/// PdfPig extractor), so no real PDF is needed. The word X-positions mirror real ČSOB and Air Bank statements;
/// names and account numbers are anonymized.
/// </summary>
public class PdfTableParserTests
{
    private static readonly PdfLayoutRegistry Registry = new();

    private static PdfTextRow Row(double y, params (string Text, double X)[] words) =>
        new(y, words.Select(w => new PdfWord(w.Text, w.X, w.X + 5, y)).ToList());

    private static ParsedStatementResult ParseCsob(params PdfTextRow[] rows) =>
        Parse("csob", rows);

    private static ParsedStatementResult ParseAirBank(params PdfTextRow[] rows) =>
        Parse("airbank", rows);

    private static ParsedStatementResult Parse(string layoutId, PdfTextRow[] rows)
    {
        var layout = layoutId == "csob" ? Registry.Resolve(BankProvider.Csob, []) : Registry.Resolve(BankProvider.AirBank, []);
        var parsed = new PdfTableParser().Parse([new PdfTextPage(rows)], layout!);
        return new ParsedStatementResult(parsed);
    }

    private sealed record ParsedStatementResult(Application.Statements.ParsedStatement Statement)
    {
        public IReadOnlyList<Application.Statements.ParsedTransaction> Tx => Statement.Transactions;
    }

    // ---- ČSOB --------------------------------------------------------------------------------------

    private static PdfTextRow CsobPeriod() => Row(762,
        ("Období:", 34), ("1.", 97), ("5.", 107), ("2025", 117), ("-", 139), ("31.", 145), ("7.", 160), ("2025", 170));

    [Fact]
    public void Csob_parses_amount_separately_from_balance_and_infers_the_year()
    {
        var result = ParseCsob(
            CsobPeriod(),
            // credit: amount column (x≈454/465) is distinct from the running balance (x≈526/537)
            Row(399, ("02.07.", 34), ("Příchozí", 63), ("úhrada", 96), ("okamžitá", 125),
                ("JAN", 227), ("NOVÁK", 255), ("831", 401), ("25", 454), ("000,00", 465), ("25", 526), ("000,00", 537)),
            Row(390, ("111111111/0300", 63)),
            // debit: negative amount, money out
            Row(376, ("03.07.", 34), ("Odchozí", 63), ("úhrada", 96), ("okamžitá", 125),
                ("Jan", 227), ("Novák", 253), ("833", 401), ("-1", 451), ("000,00", 465), ("24", 530), ("000,00", 537)),
            Row(367, ("222222222/0300", 63)),
            // footer prose must not be folded into the last transaction
            Row(329, ("Prosíme", 34), ("Vás", 66), ("o", 83), ("včasné", 90), ("překontrolování", 119)));

        Assert.Equal(2, result.Tx.Count);

        var credit = result.Tx[0];
        Assert.Equal(new DateOnly(2025, 7, 2), credit.BookingDate); // year inferred from the period
        Assert.Equal(25000.00m, credit.Amount);
        Assert.Equal("JAN NOVÁK", credit.CounterpartyName);
        Assert.Equal("111111111/0300", credit.CounterpartyAccount);
        Assert.Equal("Příchozí úhrada okamžitá", credit.Description);
        Assert.Null(credit.ValueDate);

        var debit = result.Tx[1];
        Assert.Equal(new DateOnly(2025, 7, 3), debit.BookingDate);
        Assert.Equal(-1000.00m, debit.Amount); // negative -> money out
        Assert.Equal("222222222/0300", debit.CounterpartyAccount);
    }

    // ---- Air Bank ----------------------------------------------------------------------------------

    private static PdfTextRow AirBankPeriod() => Row(518,
        ("Období", 60), ("výpisu:", 95), ("1.", 129), ("1.", 140), ("2026", 150), ("-", 174), ("31.", 179), ("1.", 195), ("2026", 205));

    [Fact]
    public void AirBank_card_payment_uses_merchant_as_counterparty_and_keeps_both_dates()
    {
        var result = ParseAirBank(
            AirBankPeriod(),
            Row(330, ("02.01.2026", 61), ("Platba", 109), ("kartou", 131), ("Jan", 187), ("Novák", 210),
                ("ACME", 325), ("SHOP", 354), ("1st", 423), ("Floor,", 434), ("-576,24", 496), ("0,00", 549)),
            Row(318, ("30.12.2025", 61), ("146309189992", 109), ("516844******7607", 187), ("Some", 325), ("Street", 360)),
            Row(295, ("23,00", 325), ("EUR", 346), ("kurz:", 362), ("25,054", 380)));

        var tx = Assert.Single(result.Tx);
        Assert.Equal(-576.24m, tx.Amount);
        Assert.Equal(new DateOnly(2026, 1, 2), tx.BookingDate);   // Zaúčtování
        Assert.Equal(new DateOnly(2025, 12, 30), tx.ValueDate);   // Provedení
        Assert.Equal("ACME SHOP 1st Floor,", tx.CounterpartyName); // merchant from Detaily, not the cardholder
        Assert.Null(tx.CounterpartyAccount);                       // masked card number is not an account
        Assert.Contains("Platba kartou", tx.Description!);
        Assert.Contains("ACME SHOP", tx.Description!);
    }

    [Fact]
    public void AirBank_extracts_inline_symbols_and_counterparty_account()
    {
        var result = ParseAirBank(
            AirBankPeriod(),
            Row(587, ("09.01.2026", 61), ("Příchozí", 109), ("úhrada", 136), ("Seyfor,", 187), ("a.", 212), ("s.", 219),
                ("VS1225", 325), ("/", 353), ("KS118", 357), ("/", 380), ("SS9999999999", 384), ("34", 488), ("087,00", 498), ("0,00", 549)),
            Row(576, ("09.01.2026", 61), ("146885257642", 109), ("7782532002", 187), ("/", 229), ("5500", 233)));

        var tx = Assert.Single(result.Tx);
        Assert.Equal(34087.00m, tx.Amount);
        Assert.Equal("Seyfor, a. s.", tx.CounterpartyName);
        Assert.Equal("7782532002 / 5500", tx.CounterpartyAccount);
        Assert.Equal("1225", tx.VariableSymbol);
        Assert.Equal("118", tx.ConstantSymbol);
        Assert.Equal("9999999999", tx.SpecificSymbol);
    }

    [Fact]
    public void AirBank_stops_at_footer_and_reconciles_a_small_run()
    {
        var result = ParseAirBank(
            AirBankPeriod(),
            Row(330, ("02.01.2026", 61), ("Platba", 109), ("kartou", 131), ("Jan", 187), ("Novák", 210),
                ("ACME", 325), ("SHOP", 354), ("-576,24", 496), ("0,00", 549)),
            Row(318, ("30.12.2025", 61), ("146309189992", 109), ("516844******7607", 187)),
            Row(171, ("16.01.2026", 61), ("Odchozí", 109), ("úhrada", 137), ("Špecle", 325), ("-210,00", 496), ("0,00", 549)),
            Row(159, ("16.01.2026", 61), ("147563679162", 109), ("2602678133", 187), ("/", 229), ("0800", 233)),
            // footer: a non-date value in the date column ends the table
            Row(81, ("Pokračování", 60), ("na", 113), ("straně", 126), ("2", 154)));

        Assert.Equal(2, result.Tx.Count);
        Assert.Equal(-786.24m, result.Tx.Sum(t => t.Amount));

        var outgoing = result.Tx[1];
        Assert.Equal(-210.00m, outgoing.Amount);
        Assert.Equal("2602678133 / 0800", outgoing.CounterpartyAccount);
        Assert.Equal(new DateOnly(2026, 1, 16), outgoing.BookingDate);
    }

    [Fact]
    public void Unknown_bank_is_not_detected_so_the_importer_can_fall_back()
    {
        var detected = Registry.Detect([new PdfTextPage([Row(100, ("Random", 10), ("text", 50))])]);
        Assert.Null(detected);
    }
}
