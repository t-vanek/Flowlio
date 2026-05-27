using Flowlio.Domain;
using Flowlio.Shared;

namespace Flowlio.Client.Services;

/// <summary>
/// Loads the signed-in member (and the permissions their role grants) once and caches it for the
/// session, so components can hide actions the user is not allowed to perform.
/// </summary>
public sealed class CurrentUserState(FlowlioApi api)
{
    private CurrentUserDto? _me;

    public async Task<CurrentUserDto?> GetAsync() => _me ??= await api.GetMeAsync();

    public async Task<bool> CanAsync(Permission permission) =>
        (await GetAsync())?.Can(permission) ?? false;
}
