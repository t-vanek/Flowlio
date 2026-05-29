using Flowlio.Application.Statements;

namespace Flowlio.Application.Abstractions;

/// <summary>
/// Access to a bank's Open Banking (PSD2) data through an aggregator (Enable Banking). The application
/// layer depends only on this abstraction; the concrete aggregator, its authentication and the mapping of
/// the aggregator's transaction shape to <see cref="ParsedTransaction"/> live in the infrastructure layer.
/// Each call is told which <see cref="BankProviderCredentials"/> to use, because credentials are per-family
/// (every family registers its own Enable Banking application — "bring your own credentials").
/// </summary>
public interface IBankDataProvider
{
    /// <summary>Lists the banks (ASPSPs) available for a country, e.g. "CZ".</summary>
    Task<IReadOnlyList<BankAspsp>> ListBanksAsync(
        BankProviderCredentials credentials, string country, CancellationToken cancellationToken = default);

    /// <summary>Starts a consent authorisation for a bank; returns the URL the user must visit to complete
    /// strong customer authentication. <paramref name="state"/> is echoed back on the redirect callback.</summary>
    Task<BankAuthorizationStart> StartAuthorizationAsync(
        BankProviderCredentials credentials, string aspspName, string country, string state,
        DateTimeOffset accessValidUntil, CancellationToken cancellationToken = default);

    /// <summary>Exchanges the authorisation <paramref name="code"/> from the callback for a session that
    /// exposes the authorised accounts.</summary>
    Task<BankAuthorizationSession> CreateSessionAsync(
        BankProviderCredentials credentials, string code, CancellationToken cancellationToken = default);

    /// <summary>Fetches booked transactions for an account on/after <paramref name="dateFrom"/>, already
    /// mapped to the canonical <see cref="ParsedTransaction"/> shape (signed amounts, symbols extracted).</summary>
    Task<IReadOnlyList<ParsedTransaction>> FetchTransactionsAsync(
        BankProviderCredentials credentials, string accountUid, DateOnly dateFrom,
        CancellationToken cancellationToken = default);
}

/// <summary>One family's Enable Banking application credentials, used to mint the request-signing JWT.</summary>
public sealed record BankProviderCredentials(string ApplicationId, string PrivateKeyPem);

/// <summary>A bank exposed by the aggregator for a given country.</summary>
public sealed record BankAspsp(string Name, string Country);

/// <summary>The result of starting a consent authorisation.</summary>
public sealed record BankAuthorizationStart(string AuthorizationUrl, string AuthorizationId);

/// <summary>An authorised aggregator session and the accounts it grants access to.</summary>
public sealed record BankAuthorizationSession(
    string SessionId, IReadOnlyList<BankAccountRef> Accounts, DateTimeOffset? ConsentValidUntil);

/// <summary>One account reachable through an authorised session.</summary>
public sealed record BankAccountRef(string Uid, string? Iban, string? Name);

/// <summary>Raised when the PSD2 consent has expired and the user must re-authorise. Lets handlers react
/// (mark the connection expired, notify the user) without knowing the aggregator's HTTP status codes.</summary>
public sealed class BankConsentExpiredException(string message) : Exception(message);
