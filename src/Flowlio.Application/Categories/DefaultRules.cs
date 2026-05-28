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

        Add("Potraviny", "Albert", "Lidl", "Kaufland", "Billa", "Tesco", "Penny", "Globus", "Makro", "Rohlik");
        Add("Doprava", "Shell", "OMV", "Benzina", "MOL", "EuroOil", "RegioJet", "Leo Express", "Uber", "Bolt");
        Add("Restaurace", "McDonald", "KFC", "Burger King", "Starbucks");
        Add("Předplatné", "Netflix", "Spotify", "HBO", "Disney", "YouTube Premium", "Apple.com/Bill");
        Add("Zábava", "Steam", "PlayStation", "Xbox", "Cinema City");
        Add("Zdraví", "Dr.Max", "Benu", "Pilulka", "Lékárna");
        Add("Oblečení", "Zara", "Reserved", "Notino", "Zalando");
        Add("Mzda", "Mzda", "Výplata");

        return rules;
    }
}
