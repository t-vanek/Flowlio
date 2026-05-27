using System.Text;
using Flowlio.Domain;

namespace Flowlio.Infrastructure.Statements;

/// <summary>
/// The single source of truth for the supported banks' tabular layouts. Holds every <see cref="BankProfile"/>,
/// resolves one from the account's bank (until the enum is migrated to profile ids), exposes the fallback
/// universal profile, and the union of all header tokens used to locate the header row across formats.
/// </summary>
internal sealed class BankProfileRegistry
{
    private readonly Dictionary<string, BankProfile> _byId;
    private readonly Dictionary<BankProvider, BankProfile> _byEnum;

    public IReadOnlyList<BankProfile> All { get; }
    public BankProfile Universal { get; }
    public IReadOnlySet<string> KnownHeaderTokens { get; }

    public BankProfileRegistry()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var fio = Fio();
        var revolut = Revolut();
        var czech = Czech();
        var universal = BuildUniversal();

        All = [fio, revolut, czech, universal];
        Universal = universal;

        _byId = All.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

        // Until the BankProvider enum is migrated to string profile ids, bridge each enum value to a profile.
        // The four mainstream Czech banks share one generic Czech layout.
        _byEnum = new Dictionary<BankProvider, BankProfile>
        {
            [BankProvider.Fio] = fio,
            [BankProvider.Revolut] = revolut,
            [BankProvider.KomercniBanka] = czech,
            [BankProvider.CeskaSporitelna] = czech,
            [BankProvider.Csob] = czech,
            [BankProvider.AirBank] = czech,
        };

        KnownHeaderTokens = BuildKnownTokens(All);
    }

    public BankProfile? ById(string id) => _byId.GetValueOrDefault(id);

    public BankProfile ForEnum(BankProvider bank) =>
        _byEnum.TryGetValue(bank, out var profile) ? profile : Universal;

    private static IReadOnlySet<string> BuildKnownTokens(IReadOnlyList<BankProfile> profiles)
    {
        var set = new HashSet<string>();
        foreach (var profile in profiles)
        {
            foreach (var header in profile.Fields.AllHeaders.Concat(profile.Amount.AllHeaders))
                set.Add(StatementText.Normalize(header));
        }
        return set;
    }

    private static Encoding Win1250() => Encoding.GetEncoding(1250);

    // ---- Shared Czech candidate spellings ----------------------------------------------------------

    private static readonly string[] CzechDate =
        ["datum", "datum zauctovani", "datum odepsani", "datum splatnosti", "datum provedeni", "datum a cas zauctovani"];

    private static readonly string[] CzechSignedAmount = ["objem", "castka", "castka v mene uctu", "suma"];
    private static readonly string[] CzechDebit = ["odepsano", "vydaj", "na vrub", "ma dati", "debet"];
    private static readonly string[] CzechCredit = ["pripsano", "prijem", "ve prospech", "dal", "kredit"];

    private static readonly string[] CzechCurrency = ["mena", "mena uctu"];
    private static readonly string[] CzechName =
        ["nazev protiuctu", "nazev uctu protistrany", "protistrana", "nazev protistrany"];
    private static readonly string[] CzechAccount =
        ["protiucet", "cislo protiuctu", "cislo uctu protistrany", "protiucet a kod banky"];
    private static readonly string[] Vs = ["vs", "variabilni symbol"];
    private static readonly string[] Ks = ["ks", "konstantni symbol"];
    private static readonly string[] Ss = ["ss", "specificky symbol"];
    private static readonly string[] CzechDescription =
        ["zprava pro prijemce", "poznamka", "poznamka pro me", "popis transakce", "popis prikazce", "uzivatelska identifikace", "nazev operace", "popis"];

    private static BankProfile Fio() => new()
    {
        Id = "fio",
        DisplayName = "Fio banka",
        CsvDelimiter = ';',
        Encoding = Encoding.UTF8,
        DateFormats = ["dd.MM.yyyy", "d.M.yyyy"],
        DecimalComma = true,
        Fields = new FieldMap
        {
            Date = ["datum"],
            Currency = ["mena"],
            CounterpartyName = ["nazev protiuctu"],
            CounterpartyAccount = ["protiucet"],
            VariableSymbol = ["vs"],
            ConstantSymbol = ["ks"],
            SpecificSymbol = ["ss"],
            Description = ["zprava pro prijemce", "poznamka", "uzivatelska identifikace"],
        },
        Amount = new AmountConvention { Signed = ["objem"] },
        Fingerprint = new StatementFingerprint { RequiredHeaders = ["objem", "vs"] },
    };

    private static BankProfile Revolut() => new()
    {
        Id = "revolut",
        DisplayName = "Revolut",
        CsvDelimiter = ',',
        Encoding = Encoding.UTF8,
        DateFormats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd"],
        DecimalComma = false,
        Fields = new FieldMap
        {
            Date = ["completed date", "started date"],
            Currency = ["currency"],
            CounterpartyName = ["description"],
            Description = ["description", "type"],
        },
        Amount = new AmountConvention { Signed = ["amount"] },
        Fingerprint = new StatementFingerprint { RequiredHeaders = ["completed date", "amount", "state"] },
    };

    private static BankProfile Czech() => new()
    {
        Id = "czech",
        DisplayName = "Česká banka (obecný formát)",
        CsvDelimiter = null,
        Encoding = Win1250(),
        DateFormats = ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd"],
        DecimalComma = true,
        Fields = new FieldMap
        {
            Date = CzechDate,
            Currency = CzechCurrency,
            CounterpartyName = CzechName,
            CounterpartyAccount = CzechAccount,
            VariableSymbol = Vs,
            ConstantSymbol = Ks,
            SpecificSymbol = Ss,
            Description = CzechDescription,
        },
        Amount = new AmountConvention { Signed = CzechSignedAmount, Debit = CzechDebit, Credit = CzechCredit },
        Fingerprint = new StatementFingerprint { RequiredHeaders = ["castka", "protiucet"] },
    };

    /// <summary>Format-agnostic fallback that tries every known spelling and both amount conventions.</summary>
    private static BankProfile BuildUniversal() => new()
    {
        Id = "universal",
        DisplayName = "Jiná banka",
        CsvDelimiter = null,
        Encoding = null,
        DateFormats = ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss"],
        DecimalComma = true,
        Fields = new FieldMap
        {
            Date = [.. CzechDate, "completed date", "started date"],
            Currency = [.. CzechCurrency, "currency"],
            CounterpartyName = [.. CzechName, "description"],
            CounterpartyAccount = CzechAccount,
            VariableSymbol = Vs,
            ConstantSymbol = Ks,
            SpecificSymbol = Ss,
            Description = [.. CzechDescription, "description", "type"],
        },
        Amount = new AmountConvention
        {
            Signed = [.. CzechSignedAmount, "amount"],
            Debit = CzechDebit,
            Credit = CzechCredit,
        },
    };
}
