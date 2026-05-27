namespace Flowlio.Application.Abstractions;

/// <summary>
/// A single transactional e-mail addressed to one recipient. The sender (From) is supplied by the
/// transport configuration, so callers only describe the recipient and content.
/// </summary>
public sealed record EmailMessage
{
    public required string ToEmail { get; init; }

    /// <summary>Display name for the recipient; falls back to the address when omitted.</summary>
    public string? ToName { get; init; }

    public required string Subject { get; init; }

    public required string HtmlBody { get; init; }

    /// <summary>Optional plain-text alternative for clients that do not render HTML.</summary>
    public string? TextBody { get; init; }
}
