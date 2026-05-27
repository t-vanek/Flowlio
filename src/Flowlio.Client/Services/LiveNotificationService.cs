using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.SignalR.Client;

namespace Flowlio.Client.Services;

public sealed record ImportCompletedNotification(Guid ImportBatchId, int ImportedCount, int DuplicateCount);

public sealed record NoticeNotification(string Message, string Severity);

/// <summary>Maintains the SignalR connection to the notifications hub, authenticated with the OIDC access token.</summary>
public sealed class LiveNotificationService(NavigationManager navigation, IAccessTokenProvider tokenProvider) : IAsyncDisposable
{
    private HubConnection? _connection;

    public event Action<ImportCompletedNotification>? ImportCompleted;

    /// <summary>Raised when the server signals that the current user's effective access changed.</summary>
    public event Action? AccessChanged;

    /// <summary>Raised when the server sends a human-readable notice to show as a toast.</summary>
    public event Action<NoticeNotification>? Notice;

    public async Task StartAsync()
    {
        if (_connection is not null)
            return;

        _connection = new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri("hubs/notifications"), options =>
            {
                options.AccessTokenProvider = async () =>
                {
                    var result = await tokenProvider.RequestAccessToken();
                    return result.TryGetToken(out var token) ? token.Value : null;
                };
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<ImportCompletedNotification>("ImportCompleted", notification => ImportCompleted?.Invoke(notification));
        _connection.On("AccessChanged", () => AccessChanged?.Invoke());
        _connection.On<NoticeNotification>("Notice", notice => Notice?.Invoke(notice));

        await _connection.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
