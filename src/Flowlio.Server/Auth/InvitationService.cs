using System.Net;
using Flowlio.Application.Abstractions;
using Flowlio.Domain;
using Flowlio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flowlio.Server.Auth;

/// <summary>Validates and accepts family invitations, linking the accepting user to a pending member.</summary>
public sealed class InvitationService(ApplicationDbContext db, IEmailSender email, ILogger<InvitationService> logger)
{
    public enum AcceptOutcome { Accepted, NotFound, Expired, AlreadyUsed }

    /// <summary>
    /// Sends the invitation e-mail containing the registration link. Failures are logged rather than
    /// thrown: the link is also returned to the inviter, so a transient SMTP outage never blocks inviting.
    /// </summary>
    public async Task SendInvitationEmailAsync(
        string toEmail, string memberName, string familyName, string inviteUrl, CancellationToken ct = default)
    {
        var message = new EmailMessage
        {
            ToEmail = toEmail,
            ToName = memberName,
            Subject = $"Pozvánka do rodiny {familyName} ve Flowlio",
            HtmlBody = $"""
                <p>Dobrý den, {WebUtility.HtmlEncode(memberName)},</p>
                <p>byli jste pozváni do rodiny <strong>{WebUtility.HtmlEncode(familyName)}</strong> ve Flowlio.
                Pro vytvoření vlastního přihlášení otevřete následující odkaz:</p>
                <p><a href="{WebUtility.HtmlEncode(inviteUrl)}">Přijmout pozvánku</a></p>
                <p>Odkaz je platný 14 dní. Pokud jste o pozvánku nežádali, tento e-mail ignorujte.</p>
                """,
            TextBody = $"Byli jste pozváni do rodiny {familyName} ve Flowlio. Pozvánku přijměte zde: {inviteUrl}",
        };

        try
        {
            await email.SendAsync(message, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send invitation e-mail to {Email}; the invite link is still available to the inviter.", toEmail);
        }
    }

    /// <summary>Looks up a pending, unexpired invitation by its raw token; null if none matches.</summary>
    public async Task<FamilyInvitation?> FindPendingAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return null;

        var hash = InvitationTokens.Hash(rawToken);
        return await db.FamilyInvitations
            .Include(i => i.Member)
            .FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
    }

    /// <summary>Accepts the invitation for the given raw token, linking it to <paramref name="userId"/>.</summary>
    public async Task<AcceptOutcome> AcceptAsync(string rawToken, Guid userId, CancellationToken ct = default)
    {
        var invitation = await FindPendingAsync(rawToken, ct);
        if (invitation is null || invitation.Member is null)
            return AcceptOutcome.NotFound;

        if (invitation.Status != InvitationStatus.Pending)
            return AcceptOutcome.AlreadyUsed;

        if (invitation.ExpiresAt < DateTimeOffset.UtcNow)
        {
            invitation.Status = InvitationStatus.Expired;
            await db.SaveChangesAsync(ct);
            return AcceptOutcome.Expired;
        }

        invitation.Member.UserId = userId;
        invitation.Member.UpdatedAt = DateTimeOffset.UtcNow;
        invitation.Status = InvitationStatus.Accepted;
        invitation.AcceptedByUserId = userId;
        invitation.AcceptedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return AcceptOutcome.Accepted;
    }
}
