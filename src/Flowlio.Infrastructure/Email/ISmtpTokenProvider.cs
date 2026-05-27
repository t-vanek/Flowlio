namespace Flowlio.Infrastructure.Email;

/// <summary>Supplies (and caches) the OAuth2 access token the SMTP client presents via XOAUTH2.</summary>
public interface ISmtpTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
