using System.Net;
using Flowlio.Application.Abstractions;
using Flowlio.Infrastructure.Identity;
using Flowlio.Server.Auth;
using Microsoft.AspNetCore.SignalR;

namespace Flowlio.Server.Realtime;

/// <summary>
/// Notifies a user about an administrative action on their account: a live toast (if connected) plus
/// a transactional e-mail for security-relevant events. Failures are logged, never thrown.
/// </summary>
public sealed class AccountNotifier(
    IHubContext<NotificationsHub> hub, IEmailSender email, ILogger<AccountNotifier> logger)
{
    public async Task NotifyAsync(
        ApplicationUser user, string subject, string message, string severity = "info", CancellationToken ct = default)
    {
        try
        {
            await hub.Clients
                .Group(NotificationsHub.UserGroup(user.Id))
                .SendAsync(NotificationsHub.Notice, new { Message = message, Severity = severity }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push live notice to user {UserId}.", user.Id);
        }

        if (string.IsNullOrWhiteSpace(user.Email))
            return;

        try
        {
            await email.SendAsync(new EmailMessage
            {
                ToEmail = user.Email,
                ToName = user.DisplayName,
                Subject = subject,
                HtmlBody = EmailLayout.Wrap($"<p>{WebUtility.HtmlEncode(message)}</p>"),
                TextBody = message,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send account notification e-mail to {Email}.", user.Email);
        }
    }
}
