using Flowlio.Domain;
using Flowlio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flowlio.Server.Auth;

/// <summary>Validates and accepts family invitations, linking the accepting user to a pending member.</summary>
public sealed class InvitationService(ApplicationDbContext db)
{
    public enum AcceptOutcome { Accepted, NotFound, Expired, AlreadyUsed }

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
