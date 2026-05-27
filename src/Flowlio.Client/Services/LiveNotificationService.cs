using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.SignalR.Client;

namespace Flowlio.Client.Services;

public sealed record ImportCompletedNotification(Guid ImportBatchId, int ImportedCount, int DuplicateCount);

/// <summary>Maintains the SignalR connection to the notifications hub, authenticated with the OIDC access token.</summary>
public sealed class LiveNotificationService(NavigationManager navigation, IAccessTokenProvider tokenProvider) : IAsyncDisposable
{
    private HubConnection? _connection;

    public event Action<ImportCompletedNotification>? ImportCompleted;

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

        await _connection.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
