using Flowlio.Domain;

namespace Flowlio.Application.Categories;

/// <summary>
/// Starter categorization rules seeded for every new family, keyed to the default categories by name.
/// They match common Czech merchants across all text fields (<see cref="RuleMatchField.Any"/>) so card
/// payments get categorized out of the box. Low priority and fully editable — a sensible head start, not law.
/// </summary>
public static class DefaultRules
{
    public static IReadOnlyList<CategorizationRule> Create(Guid familyId, IReadOnlyList<Category> categories)
    {
        var idByName = categories.ToDictionary(c => c.Name, c => c.Id);
        var rules = new List<CategorizationRule>();

        void AddMode(RuleMatchMode mode, string categoryName, params string[] patterns)
        {
            if (!idByName.TryGetValue(categoryName, out var categoryId))
                return;
            foreach (var pattern in patterns)
                rules.Add(new CategorizationRule
                {
                    FamilyId = familyId,
                    Field = RuleMatchField.Any,
                    MatchMode = mode,
                    Pattern = pattern,
                    CategoryId = categoryId,
                    Priority = 0,
                    IsActive = true,
                });
        }

        void Add(string categoryName, params string[] patterns) =>
            AddMode(RuleMatchMode.Substring, categoryName, patterns);

        // Short patterns that would false-match as substrings ("Plat" in "platba", "PID" in "rapid")
        // are matched as whole words instead.
        void AddWord(string categoryName, params string[] patterns) =>
            AddMode(RuleMatchMode.WholeWord, categoryName, patterns);

        Add("Potraviny", "Albert", "Lidl", "Kaufland", "Billa", "Tesco", "Penny", "Globus", "Makro",
            "Rohlik", "Košík", "Žabka", "Norma");
        Add("Doprava", "Shell", "OMV", "Benzina", "EuroOil", "RegioJet", "Leo Express", "Uber", "Bolt",
            "FlixBus", "Dopravní podnik", "ČD ", "České dráhy", "Litačka", "Lítačka");
        AddWord("Doprava", "PID", "MOL");
        Add("Restaurace", "McDonald", "KFC", "Burger King", "Starbucks", "Costa Coffee", "Wolt",
            "Foodora", "Damejidlo", "Dámejídlo", "Restaurace", "Bistro", "Kavárna");
        Add("Předplatné", "Netflix", "Spotify", "HBO", "Disney", "YouTube Premium", "Apple.com/Bill",
            "Google Storage", "Microsoft 365", "iCloud", "Amazon Prime");
        Add("Zábava", "Steam", "PlayStation", "Xbox", "Cinema City", "Cinestar", "Bandzone",
            "Ticketportal", "GoOut", "Knihy Dobrovský", "Luxor");
        AddWord("Zábava", "Aero");
        Add("Zdraví", "Dr.Max", "Benu", "Pilulka", "Lékárna", "dm drogerie", "Rossmann",
            "Notino", "MojeLékárna", "Zdravotní pojišťovna", "VZP");
        AddWord("Zdraví", "Teta");
        Add("Oblečení", "Zara", "Reserved", "Zalando", "H&M", "C&A", "Sinsay", "About You",
            "Deichmann", "Decathlon", "Sportisimo");
        AddWord("Oblečení", "CCC");
        Add("Bydlení", "ČEZ", "E.ON", "innogy", "Pražská energetika", "Pražská plynárenská",
            "Veolia", "Vodárny", "Vodafone", "T-Mobile", "Starnet", "Nájem", "Nájemné",
            "SVJ", "Společenství vlastníků", "Hypotéka", "IKEA", "OBI", "Hornbach", "Bauhaus");
        AddWord("Bydlení", "PRE", "O2");
        Add("Děti", "Mateřská škola", "Mateřská školka", "Školka", "Školné", "Družina", "Jídelna",
            "Bambule", "Pompo", "Sparkys");
        Add("Ostatní výdaje", "Alza", "Mall.cz", "Datart", "Notino.cz", "Poplatek", "Bankovní poplatek",
            "Výběr z bankomatu", "Výběr hotovosti", "Pojištění", "Pojišťovna", "Allianz", "Kooperativa",
            "Generali", "ČSOB Pojišťovna");
        Add("Mzda", "Mzda", "Výplata", "Odměna");
        AddWord("Mzda", "Plat");
        Add("Ostatní příjmy", "Připsání úroku", "Vratka", "Přídavek", "Dividenda");
        AddWord("Ostatní příjmy", "Úrok");

        return rules;
    }
}
