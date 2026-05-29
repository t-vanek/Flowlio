using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Flowlio.Infrastructure.Banking;

/// <summary>
/// Mints the self-signed JWT (RS256) that authenticates Enable Banking requests. Because credentials are
/// per user, the token is built from the supplied Application ID and RSA private key and cached per
/// Application ID until shortly before expiry. Signing uses the BCL directly (no JWT package needed).
/// </summary>
internal sealed class EnableBankingTokenProvider
{
    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresAt)> _cache = new();

    public string GetToken(string applicationId, string privateKeyPem)
    {
        if (_cache.TryGetValue(applicationId, out var cached) && DateTimeOffset.UtcNow < cached.ExpiresAt)
            return cached.Token;

        var now = DateTimeOffset.UtcNow;
        // Enable Banking allows up to 24h; renew a few minutes early to avoid edge expiry mid-request.
        var expires = now.AddHours(23);
        var token = BuildJwt(applicationId, privateKeyPem, now, expires);
        _cache[applicationId] = (token, expires.AddMinutes(-5));
        return token;
    }

    private static string BuildJwt(string applicationId, string privateKeyPem, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        var header = new Dictionary<string, object> { ["typ"] = "JWT", ["alg"] = "RS256", ["kid"] = applicationId };
        var payload = new Dictionary<string, object>
        {
            ["iss"] = "enablebanking.com",
            ["aud"] = "api.enablebanking.com",
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
        };

        var signingInput =
            $"{Base64Url(JsonSerializer.SerializeToUtf8Bytes(header))}.{Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload))}";

        using var rsa = CreateRsa(privateKeyPem);
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
