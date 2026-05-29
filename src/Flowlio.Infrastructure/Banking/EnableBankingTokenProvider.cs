using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Flowlio.Infrastructure.Banking;

/// <summary>
/// Mints and caches the self-signed JWT (RS256) that authenticates every Enable Banking request. The token
/// is signed with the configured RSA private key, carries the Application ID as its <c>kid</c>, and is reused
/// until shortly before expiry. Signing uses the BCL directly (no JWT package needed).
/// </summary>
internal sealed class EnableBankingTokenProvider(IOptions<EnableBankingOptions> options)
{
    private readonly EnableBankingOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _expiresAt)
            return _token;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _expiresAt)
                return _token;

            var now = DateTimeOffset.UtcNow;
            // Enable Banking allows up to 24h; renew a few minutes early to avoid edge expiry mid-request.
            var expires = now.AddHours(23);
            _token = BuildJwt(now, expires);
            _expiresAt = expires.AddMinutes(-5);
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string BuildJwt(DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        var header = new Dictionary<string, object> { ["typ"] = "JWT", ["alg"] = "RS256", ["kid"] = _options.ApplicationId };
        var payload = new Dictionary<string, object>
        {
            ["iss"] = "enablebanking.com",
            ["aud"] = "api.enablebanking.com",
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
        };

        var signingInput =
            $"{Base64Url(JsonSerializer.SerializeToUtf8Bytes(header))}.{Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload))}";

        using var rsa = CreateRsa(_options.PrivateKeyPem);
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static RSA CreateRsa(string keyOrPath)
    {
        // Accept either the PEM contents directly or a path to a .pem file.
        var pem = !keyOrPath.Contains("-----BEGIN") && File.Exists(keyOrPath)
            ? File.ReadAllText(keyOrPath)
            : keyOrPath;

        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
