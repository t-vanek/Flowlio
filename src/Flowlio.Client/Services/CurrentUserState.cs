using Flowlio.Domain;
using Flowlio.Shared;

namespace Flowlio.Client.Services;

/// <summary>
/// Loads the signed-in member (and the permissions their role grants) once and caches it for the
/// session, so components can hide actions the user is not allowed to perform. Live push (via the
/// notifications hub) calls <see cref="RefreshAsync"/> for instant updates; a background poll at the
/// server-provided interval is a fallback for when the socket is down. Either way <see cref="Changed"/>
/// fires when the effective access changes so the UI re-renders.
/// </summary>
public sealed class CurrentUserState(FlowlioApi api) : IDisposable
{
    private const int MinPollSeconds = 10;
    private const int DefaultPollSeconds = 60;

    private CurrentUserDto? _me;
    private bool _loaded;
    private PeriodicTimer? _timer;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Raised when the cached current user changes, so the UI can update its gating.</summary>
    public event Action? Changed;

    /// <summary>True when the API reports the family membership is suspended (HTTP 403 from /api/me).</summary>
    public bool IsSuspended { get; private set; }

    public async Task<CurrentUserDto?> GetAsync()
    {
        if (!_loaded)
        {
            _me = await FetchAsync();
            _loaded = true;
            StartPolling();
        }
        return _me;
    }

    /// <summary>Re-fetches the current user and notifies subscribers (used on an explicit live signal).</summary>
    public async Task<CurrentUserDto?> RefreshAsync()
    {
        _me = await FetchAsync();
        _loaded = true;
        Changed?.Invoke();
        return _me;
    }

    public async Task<bool> CanAsync(Permission permission) =>
        (await GetAsync())?.Can(permission) ?? false;

    private async Task<CurrentUserDto?> FetchAsync()
    {
        try
        {
            var result = await api.GetMeAsync();
            IsSuspended = result.Forbidden;
            return result.User;
        }
        catch
        {
            return null; // unauthenticated or a transient failure; leave the suspended flag untouched
        }
    }

    private void StartPolling()
    {
        if (_timer is not null)
            return;
        var seconds = Math.Max(_me?.PollIntervalSeconds ?? DefaultPollSeconds, MinPollSeconds);
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        _ = PollLoopAsync();
    }

    private async Task PollLoopAsync()
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(_cts.Token))
            {
                var wasSuspended = IsSuspended;
                var latest = await FetchAsync();
                // A membership suspended (or restored) mid-session must update the UI immediately.
                if (IsSuspended != wasSuspended)
                {
                    if (IsSuspended)
                        _me = null;
                    Changed?.Invoke();
                    continue;
                }
                // Ignore a failed poll (null) so a transient error never wipes the cached access;
                // genuine removals arrive via the explicit live push -> RefreshAsync.
                if (latest is not null && !SameAccess(_me, latest))
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
