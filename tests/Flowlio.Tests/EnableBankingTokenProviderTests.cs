using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flowlio.Infrastructure.Banking;
using Xunit;

namespace Flowlio.Tests;

public class EnableBankingTokenProviderTests
{
    [Fact]
    public void GetToken_builds_a_valid_rs256_jwt_with_expected_header_and_claims()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();
        var provider = new EnableBankingTokenProvider();

        var token = provider.GetToken("app-123", pem);

        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);

        using var header = JsonDocument.Parse(Decode(parts[0]));
        Assert.Equal("JWT", header.RootElement.GetProperty("typ").GetString());
        Assert.Equal("RS256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("app-123", header.RootElement.GetProperty("kid").GetString());

        using var payload = JsonDocument.Parse(Decode(parts[1]));
        Assert.Equal("enablebanking.com", payload.RootElement.GetProperty("iss").GetString());
        Assert.Equal("api.enablebanking.com", payload.RootElement.GetProperty("aud").GetString());
        Assert.True(payload.RootElement.GetProperty("exp").GetInt64() > payload.RootElement.GetProperty("iat").GetInt64());

        // The signature must verify against the public key over "header.payload".
        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = DecodeBytes(parts[2]);
        Assert.True(rsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void GetToken_caches_per_application_id()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();
        var provider = new EnableBankingTokenProvider();

        var first = provider.GetToken("app-123", pem);
        var second = provider.GetToken("app-123", pem);

        Assert.Same(first, second);
    }

    private static string Decode(string segment) => Encoding.UTF8.GetString(DecodeBytes(segment));

    private static byte[] DecodeBytes(string segment)
    {
        var padded = segment.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch { 2 => padded + "==", 3 => padded + "=", _ => padded };
        return Convert.FromBase64String(padded);
    }
}
