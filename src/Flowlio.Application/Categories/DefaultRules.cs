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

        void Add(string categoryName, params string[] patterns)
        {
            if (!idByName.TryGetValue(categoryName, out var categoryId))
                return;
            foreach (var pattern in patterns)
                rules.Add(new CategorizationRule
                {
                    FamilyId = familyId,
                    Field = RuleMatchField.Any,
                    Pattern = pattern,
                    CategoryId = categoryId,
                    Priority = 0,
                    IsActive = true,
                });
        }

        Add("Potraviny", "Albert", "Lidl", "Kaufland", "Billa", "Tesco", "Penny", "Globus", "Makro",
            "Rohlik", "Košík", "Žabka", "Norma");
        Add("Doprava", "Shell", "OMV", "Benzina", "EuroOil", "RegioJet", "Leo Express", "Uber", "Bolt",
            "FlixBus", "Dopravní podnik", "ČD ", "České dráhy", "Litačka", "Lítačka", "PID");
        Add("Restaurace", "McDonald", "KFC", "Burger King", "Starbucks", "Costa Coffee", "Wolt",
            "Foodora", "Damejidlo", "Dámejídlo", "Restaurace", "Bistro", "Kavárna");
        Add("Předplatné", "Netflix", "Spotify", "HBO", "Disney", "YouTube Premium", "Apple.com/Bill",
            "Google Storage", "Microsoft 365", "iCloud", "Amazon Prime");
        Add("Zábava", "Steam", "PlayStation", "Xbox", "Cinema City", "Cinestar", "Aero", "Bandzone",
            "Ticketportal", "GoOut", "Knihy Dobrovský", "Luxor");
        Add("Zdraví", "Dr.Max", "Benu", "Pilulka", "Lékárna", "dm drogerie", "Rossmann", "Teta",
            "Notino", "MojeLékárna", "Zdravotní pojišťovna", "VZP");
        Add("Oblečení", "Zara", "Reserved", "Zalando", "H&M", "C&A", "Sinsay", "About You",
            "Deichmann", "CCC", "Decathlon", "Sportisimo");
        Add("Bydlení", "ČEZ", "E.ON", "innogy", "Pražská energetika", "PRE ", "Pražská plynárenská",
            "Veolia", "Vodárny", "O2", "Vodafone", "T-Mobile", "Starnet", "Nájem", "Nájemné",
            "SVJ", "Společenství vlastníků", "Hypotéka", "IKEA", "OBI", "Hornbach", "Bauhaus");
        Add("Děti", "Mateřská škola", "Mateřská školka", "Školka", "Školné", "Družina", "Jídelna",
            "Bambule", "Pompo", "Sparkys");
        Add("Ostatní výdaje", "Alza", "Mall.cz", "Datart", "Notino.cz", "Poplatek", "Bankovní poplatek",
            "Výběr z bankomatu", "Výběr hotovosti", "Pojištění", "Pojišťovna", "Allianz", "Kooperativa",
            "Generali", "ČSOB Pojišťovna");
        Add("Mzda", "Mzda", "Výplata", "Plat", "Odměna");
        Add("Ostatní příjmy", "Úrok", "Připsání úroku", "Vratka", "Přídavek", "Dividenda");

        return rules;
    }
}
