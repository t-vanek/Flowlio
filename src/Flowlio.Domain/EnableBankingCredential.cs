using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>
/// One user's Enable Banking application credentials ("bring your own"): the public Application ID and the
/// RSA private key, stored encrypted at rest. Each user registers their own Enable Banking application (free
/// restricted mode) and links their own accounts, so a member connects only the accounts they own and Flowlio
/// never acts as a paid AISP. Scoped to a family for cascade/cleanup, but keyed by the owning user.
/// </summary>
public class EnableBankingCredential : AuditableEntity
{
    /// <summary>The login user who owns these credentials. One credential per user.</summary>
    public Guid UserId { get; set; }

    /// <summary>The family the user belongs to, for scoping and cascade on family deletion.</summary>
    public Guid FamilyId { get; set; }

    /// <summary>Enable Banking Application ID (a.k.a. Client ID); used as the JWT <c>kid</c>.</summary>
    public required string ApplicationId { get; set; }

    /// <summary>The RSA private key (PEM), encrypted at rest. Never returned to clients and never logged.</summary>
    public required string PrivateKeyEncrypted { get; set; }
}
