using System.Net.Http.Headers;

namespace Flowlio.Client.Auth;

/// <summary>Attaches the stored access token to outgoing API requests.</summary>
public sealed class BearerHandler(TokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await tokens.EnsureLoadedAsync();
        if (!string.IsNullOrWhiteSpace(tokens.AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        return await base.SendAsync(request, cancellationToken);
    }
}
