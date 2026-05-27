using Flowlio.Application.Abstractions;
using Flowlio.Domain;
using Flowlio.Infrastructure.Identity;
using Flowlio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flowlio.Infrastructure;

/// <summary>Writes append-only audit entries, resolving the actor from the current request.</summary>
public sealed class AuditLog(
    ApplicationDbContext db, ICurrentUser user, UserManager<ApplicationUser> userManager, ILogger<AuditLog> logger) : IAuditLog
{
    public async Task RecordAsync(
        string action,
        string? targetType = null,
        string? targetId = null,
        string? targetName = null,
        Guid? familyId = null,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string? actorName = null;
            if (user.UserId is { } actorId)
            {
                var actor = await userManager.Users.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.Id == actorId, cancellationToken);
                actorName = actor?.DisplayName ?? actor?.Email;
            }

            db.AuditEntries.Add(new AuditEntry
            {
                ActorUserId = user.UserId,
                ActorName = actorName,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                TargetName = targetName,
                FamilyId = familyId,
                Details = details,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Auditing must never break the audited operation.
            logger.LogError(ex, "Failed to write audit entry for action {Action}.", action);
        }
    }
}
