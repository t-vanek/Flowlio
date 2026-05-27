using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flowlio.Infrastructure.Email;

/// <summary>
/// Obtains an access token from OpenIddict via the client-credentials grant and caches it until just
/// before expiry, so the SMTP client can authorize with a fresh bearer token (XOAUTH2) on every send.
/// </summary>
public sealed class OpenIddictSmtpTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<SmtpOptions> options,
    ILogger<OpenIddictSmtpTokenProvider> logger) : ISmtpTokenProvider
{
    public const string HttpClientName = "flowlio-smtp-oauth";

    private readonly SmtpOAuthOptions _oauth = options.Value.OAuth;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _expiresAt)
            return _token;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _expiresAt)
                return _token;

            var (token, expiresIn) = await RequestTokenAsync(cancellationToken);
            _token = token;
            // Renew a minute early to avoid handing out a token that expires mid-send.
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn - 60, 30));
            logger.LogDebug("Obtained SMTP access token for client {ClientId}, valid ~{ExpiresIn}s.", _oauth.ClientId, expiresIn);
            return token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(string Token, int ExpiresIn)> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _oauth.ClientId,
            ["client_secret"] = _oauth.ClientSecret,
            ["scope"] = _oauth.Scope,
        });

        using var response = await client.PostAsync(_oauth.TokenEndpoint, content, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);

        if (!response.IsSuccessStatusCode || payload?.AccessToken is null)
        {
            throw new InvalidOperationException(
                $"SMTP OAuth token request to '{_oauth.TokenEndpoint}' failed ({(int)response.StatusCode}): {payload?.Error ?? "no access_token in response"}.");
        }

        return (payload.AccessToken, payload.ExpiresIn ?? 3600);
    }

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }
    }
}
