using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Flowlio.Server.Realtime;

/// <summary>Pushes live updates (e.g. completed imports) to connected family members.</summary>
[Authorize]
public sealed class NotificationsHub : Hub;
