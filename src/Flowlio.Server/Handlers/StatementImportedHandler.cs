using Flowlio.Application.Statements;
using Flowlio.Server.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;

namespace Flowlio.Server.Handlers;

/// <summary>
/// Consumes <see cref="StatementImported"/> from RabbitMQ: invalidates the cached dashboard for the
/// family and pushes a live notification to the family's connected clients via SignalR.
/// </summary>
public sealed class StatementImportedHandler
{
    public static async Task Handle(
        StatementImported message,
        IHubContext<NotificationsHub> hub,
        IDistributedCache cache,
        CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(CacheKeys.Dashboard(message.FamilyId), cancellationToken);

        await hub.Clients
            .Group(NotificationsHub.FamilyGroup(message.FamilyId))
            .SendAsync("ImportCompleted", new
            {
                message.ImportBatchId,
                message.ImportedCount,
                message.DuplicateCount,
            }, cancellationToken);
    }
}
