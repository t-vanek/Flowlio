using Microsoft.JSInterop;

namespace Flowlio.Client.Auth;

/// <summary>Holds the current access/refresh tokens and persists them in browser localStorage.</summary>
public sealed class TokenProvider(IJSRuntime js)
{
    private const string AccessKey = "flowlio.access_token";
    private const string RefreshKey = "flowlio.refresh_token";

    public string? AccessToken { get; private set; }
    public string? RefreshToken { get; private set; }

    private bool _loaded;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
            return;
        AccessToken = await js.InvokeAsync<string?>("localStorage.getItem", AccessKey);
        RefreshToken = await js.InvokeAsync<string?>("localStorage.getItem", RefreshKey);
        _loaded = true;
    }

    public async Task SetTokensAsync(string accessToken, string? refreshToken)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        _loaded = true;
        await js.InvokeVoidAsync("localStorage.setItem", AccessKey, accessToken);
        if (refreshToken is not null)
            await js.InvokeVoidAsync("localStorage.setItem", RefreshKey, refreshToken);
    }

    public async Task ClearAsync()
    {
        AccessToken = null;
        RefreshToken = null;
        await js.InvokeVoidAsync("localStorage.removeItem", AccessKey);
        await js.InvokeVoidAsync("localStorage.removeItem", RefreshKey);
    }
}
