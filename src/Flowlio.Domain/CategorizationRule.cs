using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>
/// User-defined rule that auto-assigns a category to imported transactions whose
/// <see cref="Field"/> contains <see cref="Pattern"/>. Higher <see cref="Priority"/> wins.
/// </summary>
public class CategorizationRule : AuditableEntity
{
    public Guid FamilyId { get; set; }

    public RuleMatchField Field { get; set; } = RuleMatchField.Any;

    /// <summary>Case-insensitive substring matched against the chosen field.</summary>
    public required string Pattern { get; set; }

    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }

    public int Priority { get; set; }

    public bool IsActive { get; set; } = true;
}
