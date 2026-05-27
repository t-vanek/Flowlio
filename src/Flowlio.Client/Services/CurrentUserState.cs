using Flowlio.Domain;
using Flowlio.Shared;

namespace Flowlio.Client.Services;

/// <summary>
/// Loads the signed-in member (and the permissions their role grants) once and caches it for the
/// session, so components can hide actions the user is not allowed to perform. A background poll
/// re-fetches periodically and, together with the explicit <see cref="RefreshAsync"/>, raises
/// <see cref="Changed"/> whenever the user's effective access changes — so an admin grant or role
/// change made elsewhere shows up in the running session without a reload.
/// </summary>
public sealed class CurrentUserState(FlowlioApi api) : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);

    private CurrentUserDto? _me;
    private bool _loaded;
    private PeriodicTimer? _timer;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Raised when the cached current user changes, so the UI can update gating.</summary>
    public event Action? Changed;

    public async Task<CurrentUserDto?> GetAsync()
    {
        if (!_loaded)
        {
            _me = await api.GetMeAsync();
            _loaded = true;
            StartPolling();
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

    private void StartPolling()
    {
        if (_timer is not null)
            return;
        _timer = new PeriodicTimer(PollInterval);
        _ = PollLoopAsync();
    }

    private async Task PollLoopAsync()
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(_cts.Token))
            {
                CurrentUserDto? latest;
                try
                {
                    latest = await api.GetMeAsync();
                }
                catch
                {
                    continue; // transient (e.g. a token refresh in flight); retry on the next tick
                }

                if (!SameAccess(_me, latest))
                {
                    _me = latest;
                    Changed?.Invoke();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
    }

    private static bool SameAccess(CurrentUserDto? a, CurrentUserDto? b)
    {
        if (a is null || b is null)
            return ReferenceEquals(a, b);
        return a.IsAdmin == b.IsAdmin
            && a.Role == b.Role
            && a.MemberId == b.MemberId
            && a.Permissions.Count == b.Permissions.Count
            && !a.Permissions.Except(b.Permissions).Any();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _timer?.Dispose();
        _cts.Dispose();
    }
}
