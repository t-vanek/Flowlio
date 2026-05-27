using System.Text;
using Flowlio.Domain;

namespace Flowlio.Infrastructure.Statements;

/// <summary>
/// Best-effort CSV layouts for the supported Czech banks plus Revolut. Header candidates cover the
/// common export spellings; the generic matcher tolerates extra spacing and diacritics. Profiles are
/// a starting point and may need tuning against a specific export variant.
/// </summary>
public static class BankCsvProfiles
{
    private static readonly string[] DateCandidates =
        ["datum", "datum zauctovani", "datum odepsani", "datum splatnosti", "datum provedeni", "datum a cas zauctovani", "completed date", "started date"];

    private static readonly string[] AmountCandidates =
        ["objem", "castka", "castka v mene uctu", "amount", "suma"];

    private static readonly string[] CurrencyCandidates =
        ["mena", "mena uctu", "currency"];

    private static readonly string[] CounterpartyNameCandidates =
        ["nazev protiuctu", "nazev uctu protistrany", "protistrana", "nazev protistrany", "description", "popis"];

    private static readonly string[] CounterpartyAccountCandidates =
        ["protiucet", "cislo protiuctu", "cislo uctu protistrany", "protiucet a kod banky"];

    private static readonly string[] VsCandidates = ["vs", "variabilni symbol"];
    private static readonly string[] KsCandidates = ["ks", "konstantni symbol"];
    private static readonly string[] SsCandidates = ["ss", "specificky symbol"];

    private static readonly string[] DescriptionCandidates =
        ["zprava pro prijemce", "poznamka", "poznamka pro me", "popis transakce", "popis prikazce", "uzivatelska identifikace", "description", "nazev operace"];

    static BankCsvProfiles()
    {
        // Czech bank exports are frequently encoded in Windows-1250.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static Encoding Win1250() => Encoding.GetEncoding(1250);

    public static BankCsvProfile For(BankProvider bank) => bank switch
    {
        BankProvider.Fio => Fio,
        BankProvider.Revolut => Revolut,
        BankProvider.KomercniBanka => Czech(BankProvider.KomercniBanka),
        BankProvider.CeskaSporitelna => Czech(BankProvider.CeskaSporitelna),
        BankProvider.Csob => Czech(BankProvider.Csob),
        BankProvider.AirBank => Czech(BankProvider.AirBank),
        _ => Universal,
    };

    public static readonly BankCsvProfile Fio = new()
    {
        Bank = BankProvider.Fio,
        Delimiter = ';',
        Encoding = Encoding.UTF8,
        DateFormats = ["dd.MM.yyyy", "d.M.yyyy"],
        DecimalComma = true,
        DateHeaders = ["datum"],
        AmountHeaders = ["objem"],
        CurrencyHeaders = ["mena"],
        CounterpartyNameHeaders = ["nazev protiuctu"],
        CounterpartyAccountHeaders = ["protiucet"],
        VariableSymbolHeaders = ["vs"],
        ConstantSymbolHeaders = ["ks"],
        SpecificSymbolHeaders = ["ss"],
        DescriptionHeaders = ["zprava pro prijemce", "poznamka", "uzivatelska identifikace"],
    };

    public static readonly BankCsvProfile Revolut = new()
    {
        Bank = BankProvider.Revolut,
        Delimiter = ',',
        Encoding = Encoding.UTF8,
        DateFormats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd"],
        DecimalComma = false,
        DateHeaders = ["completed date", "started date"],
        AmountHeaders = ["amount"],
        CurrencyHeaders = ["currency"],
        CounterpartyNameHeaders = ["description"],
        DescriptionHeaders = ["description", "type"],
    };

    private static BankCsvProfile Czech(BankProvider bank) => new()
    {
        Bank = bank,
        Delimiter = null, // auto-detect
        Encoding = Win1250(),
        DateFormats = ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd"],
        DecimalComma = true,
        DateHeaders = DateCandidates,
        AmountHeaders = AmountCandidates,
        CurrencyHeaders = CurrencyCandidates,
        CounterpartyNameHeaders = CounterpartyNameCandidates,
        CounterpartyAccountHeaders = CounterpartyAccountCandidates,
        VariableSymbolHeaders = VsCandidates,
        ConstantSymbolHeaders = KsCandidates,
        SpecificSymbolHeaders = SsCandidates,
        DescriptionHeaders = DescriptionCandidates,
    };

    /// <summary>Format-agnostic fallback that tries every known header spelling.</summary>
    public static readonly BankCsvProfile Universal = new()
    {
        Bank = BankProvider.Other,
        Delimiter = null,
        Encoding = Encoding.UTF8,
        DateFormats = ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss"],
        DecimalComma = true,
        DateHeaders = DateCandidates,
        AmountHeaders = AmountCandidates,
        CurrencyHeaders = CurrencyCandidates,
        CounterpartyNameHeaders = CounterpartyNameCandidates,
        CounterpartyAccountHeaders = CounterpartyAccountCandidates,
        VariableSymbolHeaders = VsCandidates,
        ConstantSymbolHeaders = KsCandidates,
        SpecificSymbolHeaders = SsCandidates,
        DescriptionHeaders = DescriptionCandidates,
    };
}
