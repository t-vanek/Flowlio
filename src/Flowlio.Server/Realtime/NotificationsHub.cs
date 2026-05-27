using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OpenIddict.Validation.AspNetCore;

namespace Flowlio.Server.Realtime;

/// <summary>Pushes live updates (e.g. completed imports) to connected family members.</summary>
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public sealed class NotificationsHub : Hub;
