using System.Security.Cryptography;

namespace Flowlio.Server.Auth;

/// <summary>Generates and hashes family-invitation tokens. Only the hash is ever persisted.</summary>
public static class InvitationTokens
{
    /// <summary>Creates a new random token, returning the raw token (for the link) and its hash (for storage).</summary>
    public static (string Raw, string Hash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var raw = Base64UrlEncode(bytes);
        return (raw, Hash(raw));
    }

    public static string Hash(string rawToken)
    {
        var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexStringLower(digest);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
