using System.Net;
using Flowlio.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flowlio.Tests;

public class OpenIddictSmtpTokenProviderTests
{
    private static SmtpOptions OptionsWith(string token = "tok-abc") => new()
    {
        OAuth = new SmtpOAuthOptions
        {
            TokenEndpoint = "https://localhost:5443/connect/token",
            ClientId = "flowlio-mailer",
            ClientSecret = "secret",
            Scope = "flowlio.smtp",
        },
    };

    [Fact]
    public async Task GetAccessTokenAsync_posts_client_credentials_and_returns_token()
    {
        var handler = new StubHandler("""{"access_token":"tok-abc","expires_in":3600}""");
        var provider = new OpenIddictSmtpTokenProvider(new StubFactory(handler), Options.Create(OptionsWith()), NullLogger<OpenIddictSmtpTokenProvider>.Instance);

        var token = await provider.GetAccessTokenAsync();

        Assert.Equal("tok-abc", token);
        Assert.Contains("grant_type=client_credentials", handler.LastRequestBody);
        Assert.Contains("client_id=flowlio-mailer", handler.LastRequestBody);
        Assert.Contains("client_secret=secret", handler.LastRequestBody);
        Assert.Contains("scope=flowlio.smtp", handler.LastRequestBody);
    }

    [Fact]
    public async Task GetAccessTokenAsync_caches_token_across_calls()
    {
        var handler = new StubHandler("""{"access_token":"tok-abc","expires_in":3600}""");
        var provider = new OpenIddictSmtpTokenProvider(new StubFactory(handler), Options.Create(OptionsWith()), NullLogger<OpenIddictSmtpTokenProvider>.Instance);

        await provider.GetAccessTokenAsync();
        await provider.GetAccessTokenAsync();

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetAccessTokenAsync_throws_on_error_response()
    {
        var handler = new StubHandler("""{"error":"invalid_client"}""", HttpStatusCode.BadRequest);
        var provider = new OpenIddictSmtpTokenProvider(new StubFactory(handler), Options.Create(OptionsWith()), NullLogger<OpenIddictSmtpTokenProvider>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetAccessTokenAsync());
        Assert.Contains("invalid_client", ex.Message);
    }

    private sealed class StubHandler(string responseJson, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string LastRequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
