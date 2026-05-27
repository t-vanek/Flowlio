using MailKit.Security;

namespace Flowlio.Infrastructure.Email;

/// <summary>
/// SMTP transport configuration, bound from the <c>Smtp</c> configuration section. The client
/// authorizes to the server with an OAuth2 bearer token (XOAUTH2) obtained from OpenIddict.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 25;

    /// <summary>
    /// Connection security. <c>None</c> suits the local smtp4dev catcher; use <c>StartTls</c> or
    /// <c>SslOnConnect</c> against a real provider. Bound by name from config.
    /// </summary>
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.StartTlsWhenAvailable;

    /// <summary>Identity (login) presented in the XOAUTH2 exchange; defaults to <see cref="FromAddress"/>.</summary>
    public string? User { get; set; }

    public string FromAddress { get; set; } = "no-reply@flowlio.local";

    public string FromName { get; set; } = "Flowlio";

    /// <summary>OAuth2 client-credentials settings used to mint the SMTP access token.</summary>
    public SmtpOAuthOptions OAuth { get; set; } = new();
}

/// <summary>
/// Client-credentials settings the mailer uses to obtain an access token from OpenIddict, which is
/// then presented to the SMTP server via XOAUTH2.
/// </summary>
public sealed class SmtpOAuthOptions
{
    /// <summary>The OpenIddict token endpoint, e.g. <c>https://localhost:5443/connect/token</c>.</summary>
    public string TokenEndpoint { get; set; } = "";

    public string ClientId { get; set; } = "";

    public string ClientSecret { get; set; } = "";

    /// <summary>Scope requested for the SMTP token; its resource becomes the token audience.</summary>
    public string Scope { get; set; } = "";
}
