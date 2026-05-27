using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>A spending or income category. Supports a single level of nesting via <see cref="ParentId"/>.</summary>
public class Category : AuditableEntity
{
    public Guid FamilyId { get; set; }
    public Family? Family { get; set; }

    public required string Name { get; set; }
    public CategoryKind Kind { get; set; } = CategoryKind.Expense;

    /// <summary>Hex color used in charts and the UI, e.g. "#2563eb".</summary>
    public string Color { get; set; } = "#64748b";

    /// <summary>Optional FluentUI icon name shown next to the category.</summary>
    public string? Icon { get; set; }

    public Guid? ParentId { get; set; }
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = [];

    /// <summary>True for the default categories seeded for every new family.</summary>
    public bool IsSystem { get; set; }
}
