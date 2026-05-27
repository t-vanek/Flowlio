namespace Flowlio.Application.Abstractions;

/// <summary>
/// Records administration- and security-relevant actions to an append-only audit log. The actor is
/// resolved automatically from the current request. Implementations must never throw into the caller —
/// a failure to audit is logged but does not fail the underlying operation.
/// </summary>
public interface IAuditLog
{
    Task RecordAsync(
        string action,
        string? targetType = null,
        string? targetId = null,
        string? targetName = null,
        Guid? familyId = null,
        string? details = null,
        CancellationToken cancellationToken = default);
}
