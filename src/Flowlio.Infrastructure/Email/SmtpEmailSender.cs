using Flowlio.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Flowlio.Infrastructure.Email;

/// <summary>
/// Sends e-mail over SMTP using MailKit, authorizing to the server with an OAuth2 bearer token
/// (XOAUTH2) minted by OpenIddict rather than a static password.
/// </summary>
public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    ISmtpTokenProvider tokenProvider,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var mime = BuildMimeMessage(_options, message);
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        var user = string.IsNullOrWhiteSpace(_options.User) ? _options.FromAddress : _options.User;

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, _options.Security, cancellationToken);
        await client.AuthenticateAsync(new SaslMechanismOAuth2(user, token), cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        logger.LogInformation("Sent e-mail to {Recipient} (subject: {Subject})", message.ToEmail, message.Subject);
    }

    /// <summary>Builds the MIME message from the transport config and payload; pure, so it is unit-testable.</summary>
    public static MimeMessage BuildMimeMessage(SmtpOptions options, EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName ?? message.ToEmail, message.ToEmail));
        mime.Subject = message.Subject;

        var body = new BodyBuilder { HtmlBody = message.HtmlBody };
        if (!string.IsNullOrWhiteSpace(message.TextBody))
            body.TextBody = message.TextBody;
        mime.Body = body.ToMessageBody();

        return mime;
    }
}
