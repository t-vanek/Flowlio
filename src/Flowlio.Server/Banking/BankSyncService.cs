using Flowlio.Application.Banking;
using Flowlio.Domain;
using Flowlio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace Flowlio.Server.Banking;

/// <summary>
/// Periodically pulls new transactions for every active Open Banking connection — the "automatic import".
/// Each connection is synced through the same Wolverine command as an on-demand sync, so it runs in a
/// transaction and emits the usual completion event (dashboard cache invalidation + live notification).
/// Entirely best-effort and a no-op when Enable Banking isn't configured.
/// </summary>
internal sealed class BankSyncService(IServiceScopeFactory scopeFactory, ILogger<BankSyncService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let startup (migrations, Wolverine, RabbitMQ provisioning) settle before the first sweep.
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncDueAsync(stoppingToken);
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SyncDueAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            // Only active connections are due; each sync resolves its own user's credentials and skips itself
            // gracefully if they are missing, so there is nothing to gate on here.
            var due = await db.BankConnections
                .Where(c => c.Status == BankConnectionStatus.Active)
                .Select(c => c.Id)
                .ToListAsync(ct);

            foreach (var id in due)
            {
                try
                {
                    await bus.InvokeAsync(new SyncBankAccountCommand { BankConnectionId = id }, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Background bank sync failed for connection {ConnectionId}.", id);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Background bank sync sweep failed.");
        }
    }
}
