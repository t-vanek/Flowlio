using Flowlio.Domain;

namespace Flowlio.Infrastructure.Statements.Pdf;

/// <summary>
/// Holds the known bank PDF layouts and resolves one for a statement: the caller's bank hint wins; otherwise
/// the bank is detected from markers (BIC, header tokens) in the document text. Returns null when no layout
/// matches, so the importer can fall back to the heuristic parser.
/// </summary>
internal sealed class PdfLayoutRegistry
{
    private readonly IReadOnlyList<PdfLayout> _all;
    private readonly IReadOnlyDictionary<BankProvider, PdfLayout> _byBank;

    public PdfLayoutRegistry()
    {
        _all = [Csob(), AirBank()];
        _byBank = _all.ToDictionary(l => l.Bank);
    }

    public PdfLayout? Resolve(BankProvider bankHint, IReadOnlyList<PdfTextPage> pages)
    {
        if (bankHint != BankProvider.Other && _byBank.TryGetValue(bankHint, out var hinted))
            return hinted;

        return Detect(pages);
    }

    public PdfLayout? Detect(IReadOnlyList<PdfTextPage> pages)
    {
        var text = string.Join(
            ' ',
            pages.SelectMany(p => p.Rows).SelectMany(r => r.Words).Select(w => w.Text));

        foreach (var layout in _all)
        {
            if (layout.DetectionMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase)))
                return layout;
        }
        return null;
    }

    // ČSOB: two-line header (Datum/Označení platby/Název protiúčtu/Identifikace/Částka/Zůstatek). The date
    // column omits the year. One transaction spans two visual rows; the counterparty account sits under the
    // "Označení platby" (Description) column on the continuation row.
    private static PdfLayout Csob() => new()
    {
        Id = "csob",
        DisplayName = "ČSOB",
        Bank = BankProvider.Csob,
        Columns =
        [
            new PdfColumn(PdfField.Date, 34),
            new PdfColumn(PdfField.Description, 63),
            new PdfColumn(PdfField.CounterpartyName, 227),
            new PdfColumn(PdfField.Identification, 378),
            new PdfColumn(PdfField.Amount, 463),
            new PdfColumn(PdfField.Balance, 530),
        ],
        DetectionMarkers = ["CEKOCZPP", "Označení platby", "Přehled pohybů na účtu"],
        DateFormats = ["dd.MM.yyyy", "d.M.yyyy"],
        DateHasYear = false,
        DescriptionFields = [PdfField.Description],
        AccountSourceField = PdfField.Description,
    };

    // Air Bank: header Zaúčtování/Typ/Název/Detaily/Částka CZK/Poplatky. Dates carry the year; the second
    // date (Provedení) is the value date on the continuation row. Card payments put the cardholder in Název
    // and the merchant in Detaily; VS/KS/SS appear inline in Detaily ("VS1225 / KS118 / SS...").
    private static PdfLayout AirBank() => new()
    {
        Id = "airbank",
        DisplayName = "Air Bank",
        Bank = BankProvider.AirBank,
        Columns =
        [
            new PdfColumn(PdfField.Date, 60),
            new PdfColumn(PdfField.Description, 106),
            new PdfColumn(PdfField.CounterpartyName, 184),
            new PdfColumn(PdfField.Details, 323),
            new PdfColumn(PdfField.Amount, 483),
            new PdfColumn(PdfField.Fees, 534),
        ],
        DetectionMarkers = ["AIRACZPP", "Zaúčtování", "Kód transakce"],
        DateFormats = ["dd.MM.yyyy", "d.M.yyyy"],
        DateHasYear = true,
        DescriptionFields = [PdfField.Description, PdfField.Details],
        DescriptionSeparator = " — ",
        AccountSourceField = PdfField.CounterpartyName,
        SymbolsInline = true,
        CardCounterpartyFromDetails = true,
        CardPaymentMarker = "kartou",
    };
}
