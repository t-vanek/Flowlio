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
