using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Flowlio.Client.Services;

/// <summary>
/// Sends the user back to the login flow when the API rejects them as unauthenticated (401) or the
/// access token can no longer be acquired — e.g. the account was locked or blocked mid-session, so the
/// silent token refresh fails. Without this, a mid-session lockout would surface as broken pages
/// instead of a clean sign-out. (403/Forbidden is left alone: the user is authenticated but lacks a
/// permission, which the pages handle in place.)
/// </summary>
public sealed class AuthRedirectHandler(NavigationManager navigation) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                navigation.NavigateToLogin("authentication/login");
            return response;
        }
        catch (AccessTokenNotAvailableException ex)
        {
            ex.Redirect();
            // Navigation to the login flow is under way; hand back a 401 so callers fail quietly
            // instead of throwing an unhandled exception while the page is being torn down.
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = request };
        }
    }
}
