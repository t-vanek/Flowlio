namespace Flowlio.Application.Abstractions;

/// <summary>
/// Sends transactional e-mails (family invitations, account notifications). Implemented over SMTP in
/// the infrastructure layer so the application/server code stays transport-agnostic.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
