namespace Flowlio.Domain.Common;

/// <summary>Base type for all persisted aggregates. Uses a sequential-friendly Guid as the key.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

/// <summary>Entity that tracks creation and last-modification timestamps (UTC).</summary>
public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// An entity that is removed by marking it deleted rather than deleting the row, so its history and
/// references survive. Soft-deleted rows are hidden from normal queries by a global query filter.
/// </summary>
public interface ISoftDeletable
{
    /// <summary>When set, the entity is soft-deleted (hidden everywhere); <c>null</c> means live.</summary>
    DateTimeOffset? DeletedAt { get; set; }
}
