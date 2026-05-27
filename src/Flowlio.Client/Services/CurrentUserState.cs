using Flowlio.Domain;
using Flowlio.Shared;

namespace Flowlio.Client.Services;

/// <summary>
/// Loads the signed-in member (and the permissions their role grants) once and caches it for the
/// session, so components can hide actions the user is not allowed to perform. The cache can be
/// invalidated after an action that changes the current user's own role or permissions (e.g. an
/// ownership transfer); subscribers are notified via <see cref="Changed"/> so they can re-render.
/// </summary>
public sealed class CurrentUserState(FlowlioApi api)
{
    private CurrentUserDto? _me;
    private bool _loaded;

    /// <summary>Raised when the cached current user is refreshed, so the UI can update gating.</summary>
    public event Action? Changed;

    public async Task<CurrentUserDto?> GetAsync()
    {
        if (!_loaded)
        {
            _me = await api.GetMeAsync();
            _loaded = true;
        }
        return _me;
    }

    /// <summary>Re-fetches the current user from the server and notifies subscribers.</summary>
    public async Task<CurrentUserDto?> RefreshAsync()
    {
        _me = await api.GetMeAsync();
        _loaded = true;
        Changed?.Invoke();
        return _me;
    }

    public async Task<bool> CanAsync(Permission permission) =>
        (await GetAsync())?.Can(permission) ?? false;
}
