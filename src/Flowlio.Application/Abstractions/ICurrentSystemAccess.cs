using Flowlio.Domain;

namespace Flowlio.Application.Abstractions;

/// <summary>
/// Resolves the current user's effective <see cref="SystemPermission"/>s — the union of permissions
/// granted by their system roles, with the built-in administrator role implying all permissions.
/// </summary>
public interface ICurrentSystemAccess
{
    Task<IReadOnlySet<SystemPermission>> GetPermissionsAsync(CancellationToken cancellationToken = default);

    Task<bool> CanAsync(SystemPermission permission, CancellationToken cancellationToken = default);

    /// <summary>Whether the user holds any system permission at all (i.e. has access to administration).</summary>
    Task<bool> HasAnyAsync(CancellationToken cancellationToken = default);
}
