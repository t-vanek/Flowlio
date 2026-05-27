using System.Security.Claims;
using Flowlio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Flowlio.Server.Realtime;

/// <summary>Pushes live updates (e.g. completed imports) to connected family members.</summary>
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public sealed class NotificationsHub(ApplicationDbContext db) : Hub
{
    /// <summary>Event name the client listens for to re-fetch its effective permissions.</summary>
    public const string AccessChanged = "AccessChanged";

    public static string FamilyGroup(Guid familyId) => $"family:{familyId}";
    public static string UserGroup(Guid userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        var raw = Context.User?.FindFirstValue(Claims.Subject)
                  ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

        if (Guid.TryParse(raw, out var userId))
        {
            // Personal group lets the server notify this user directly (e.g. admin role changes).
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));

            var familyId = await db.FamilyMembers
                .Where(m => m.UserId == userId)
                .Select(m => (Guid?)m.FamilyId)
                .FirstOrDefaultAsync();

            if (familyId is { } id)
                await Groups.AddToGroupAsync(Context.ConnectionId, FamilyGroup(id));
        }

        await base.OnConnectedAsync();
    }
}

/// <summary>Convenience push helpers used by endpoints when a user's effective access changes.</summary>
public static class AccessNotifications
{
    public static Task NotifyFamilyAsync(this IHubContext<NotificationsHub> hub, Guid familyId, CancellationToken ct = default) =>
        hub.Clients.Group(NotificationsHub.FamilyGroup(familyId)).SendAsync(NotificationsHub.AccessChanged, ct);

    public static Task NotifyUserAsync(this IHubContext<NotificationsHub> hub, Guid userId, CancellationToken ct = default) =>
        hub.Clients.Group(NotificationsHub.UserGroup(userId)).SendAsync(NotificationsHub.AccessChanged, ct);
}
