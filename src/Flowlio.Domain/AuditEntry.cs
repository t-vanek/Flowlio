using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>
/// An append-only record of a security- or administration-relevant action: who did what to whom and
/// when. Never updated or deleted in normal operation.
/// </summary>
public class AuditEntry : Entity
{
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The acting user's id, or null for system-originated entries.</summary>
    public Guid? ActorUserId { get; set; }
    public string? ActorName { get; set; }

    /// <summary>Machine-readable action code, e.g. "user.block" or "system-role.update".</summary>
    public required string Action { get; set; }

    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? TargetName { get; set; }

    /// <summary>The family the action concerned, when applicable.</summary>
    public Guid? FamilyId { get; set; }

    /// <summary>Short human-readable description of the action.</summary>
    public string? Details { get; set; }
}
