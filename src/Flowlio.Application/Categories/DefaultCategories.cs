using Flowlio.Domain;

namespace Flowlio.Application.Categories;

/// <summary>Default category set seeded for every new family (Czech household budgeting).</summary>
public static class DefaultCategories
{
    public static IReadOnlyList<Category> Create(Guid familyId) =>
    [
        Expense(familyId, "Bydlení", "#2563eb", "Home"),
        Expense(familyId, "Potraviny", "#16a34a", "Food"),
        Expense(familyId, "Doprava", "#f59e0b", "VehicleCar"),
        Expense(familyId, "Restaurace", "#ef4444", "FoodPizza"),
        Expense(familyId, "Zábava", "#a855f7", "Games"),
        Expense(familyId, "Zdraví", "#06b6d4", "Heart"),
        Expense(familyId, "Děti", "#ec4899", "PeopleTeam"),
        Expense(familyId, "Oblečení", "#8b5cf6", "Shirt"),
        Expense(familyId, "Předplatné", "#0ea5e9", "Stream"),
        Expense(familyId, "Ostatní výdaje", "#64748b", "MoreHorizontal"),
        Income(familyId, "Mzda", "#22c55e", "Money"),
        Income(familyId, "Ostatní příjmy", "#10b981", "ArrowTrendingLines"),
    ];

    private static Category Expense(Guid familyId, string name, string color, string icon) => new()
    {
        FamilyId = familyId,
        Name = name,
        Kind = CategoryKind.Expense,
        Color = color,
        Icon = icon,
        IsSystem = true,
    };

    private static Category Income(Guid familyId, string name, string color, string icon) => new()
    {
        FamilyId = familyId,
        Name = name,
        Kind = CategoryKind.Income,
        Color = color,
        Icon = icon,
        IsSystem = true,
    };
}
