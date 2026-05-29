using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>
/// Records that a family dismissed a learned rule suggestion for a given counterparty + category, so it is
/// not offered again. Keyed by a normalized (trimmed, lower-cased) counterparty so case variants collapse to
/// one dismissal, matching how suggestions are grouped.
/// </summary>
public class RuleSuggestionDismissal : AuditableEntity
{
    public Guid FamilyId { get; set; }

    /// <summary>Normalized counterparty key (trimmed + lower-cased) the dismissed suggestion was built from.</summary>
    public required string CounterpartyKey { get; set; }

    public Guid CategoryId { get; set; }
}
