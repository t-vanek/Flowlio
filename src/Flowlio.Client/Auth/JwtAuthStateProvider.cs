using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace Flowlio.Client.Auth;

/// <summary>Builds the authentication state from the JWT access token stored by <see cref="TokenProvider"/>.</summary>
public sealed class JwtAuthStateProvider(TokenProvider tokens) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await tokens.EnsureLoadedAsync();
        var token = tokens.AccessToken;
        if (string.IsNullOrWhiteSpace(token) || IsExpired(token))
            return Anonymous;

        var identity = new ClaimsIdentity(ParseClaims(token), authenticationType: "jwt", nameType: "name", roleType: "role");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public void NotifyChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    private static bool IsExpired(string token)
    {
        var claims = ParseClaims(token);
        var exp = claims.FirstOrDefault(c => c.Type == "exp")?.Value;
        if (long.TryParse(exp, out var seconds))
            return DateTimeOffset.FromUnixTimeSeconds(seconds) <= DateTimeOffset.UtcNow;
        return false;
    }

    private static IEnumerable<Claim> ParseClaims(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2)
            return [];

        var payload = Decode(parts[1]);
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payload);
        if (dict is null)
            return [];

        var claims = new List<Claim>();
        foreach (var (key, value) in dict)
        {
            if (value.ValueKind == JsonValueKind.Array)
                claims.AddRange(value.EnumerateArray().Select(v => new Claim(key, v.ToString())));
            else
                claims.Add(new Claim(key, value.ToString()));
        }
        return claims;
    }

    private static string Decode(string base64Url)
    {
        var s = base64Url.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
}
