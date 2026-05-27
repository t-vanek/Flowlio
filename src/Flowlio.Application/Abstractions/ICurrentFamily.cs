namespace Flowlio.Application.Abstractions;

/// <summary>
/// Resolves the family the current user operates on, provisioning a default family with seeded
/// categories on first use so a freshly registered user has a working budget immediately.
/// </summary>
public interface ICurrentFamily
{
    Task<Guid> RequireAsync(CancellationToken cancellationToken = default);
}
