using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>
/// An invitation that lets another person join the family with their own login. It activates a
/// pre-created (pending) <see cref="FamilyMember"/> by setting its <c>UserId</c> on acceptance.
/// Only a hash of the token is stored; the raw token lives in the invite link.
/// </summary>
public class FamilyInvitation : AuditableEntity
{
    public Guid FamilyId { get; set; }
    public Family? Family { get; set; }

    /// <summary>The pending member this invitation activates once accepted.</summary>
    public Guid MemberId { get; set; }
    public FamilyMember? Member { get; set; }

    public required string Email { get; set; }

    /// <summary>SHA-256 hash (hex) of the invitation token. The raw token is never persisted.</summary>
    public required string TokenHash { get; set; }

    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

    public DateTimeOffset ExpiresAt { get; set; }

    public Guid? AcceptedByUserId { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}
