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
    public static string FamilyGroup(Guid familyId) => $"family:{familyId}";

    public override async Task OnConnectedAsync()
    {
        var raw = Context.User?.FindFirstValue(Claims.Subject)
                  ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

        if (Guid.TryParse(raw, out var userId))
        {
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
