using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>
/// A link between a <see cref="BankAccount"/> and the bank's Open Banking (PSD2) API, established through
/// the Enable Banking aggregator. Holds the aggregator session and the PSD2 consent window so transactions
/// can be pulled automatically until the consent expires and the user re-authorises.
/// </summary>
public class BankConnection : AuditableEntity, ISoftDeletable
{
    public Guid FamilyId { get; set; }

    public Guid BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    /// <summary>Enable Banking ASPSP (bank) name, e.g. "Air Bank".</summary>
    public required string AspspName { get; set; }

    /// <summary>ISO-3166 country of the ASPSP, e.g. "CZ".</summary>
    public required string AspspCountry { get; set; }

    /// <summary>Authorisation id returned when the consent flow is started.</summary>
    public string? AuthorizationId { get; set; }

    /// <summary>Unguessable token that correlates the bank's redirect callback back to this connection.</summary>
    public string? State { get; set; }

    /// <summary>Aggregator session id obtained after the user completes authentication.</summary>
    public string? SessionId { get; set; }

    /// <summary>The aggregator account identifier transactions are fetched from.</summary>
    public string? AccountUid { get; set; }

    /// <summary>When the PSD2 consent stops being valid; after this a re-authorisation is required.</summary>
    public DateTimeOffset? ConsentValidUntil { get; set; }

    public BankConnectionStatus Status { get; set; } = BankConnectionStatus.Pending;

    /// <summary>Last successful sync, used as the lower bound for the next incremental fetch.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>Message from the last failed operation, surfaced so the user knows to re-authorise.</summary>
    public string? LastError { get; set; }

    /// <summary>Identity user who started the connection.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>When set, the connection is soft-deleted (disconnected) but kept for history.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
