using System.Net;
using System.Text;
using Flowlio.Application.Abstractions;
using Flowlio.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace Flowlio.Server.Auth;

/// <summary>Builds and sends the transactional e-mails used during account sign-up.</summary>
public static class AccountEmails
{
    /// <summary>
    /// Generates an e-mail-confirmation link for the user, e-mails it, and returns the URL so the
    /// caller can surface it in Development (where no SMTP server may be running). Send failures are
    /// logged rather than thrown, so a transient SMTP outage never breaks registration.
    /// </summary>
    public static async Task<string> SendConfirmationAsync(
        PageModel page, UserManager<ApplicationUser> users, IEmailSender email, ILogger logger, ApplicationUser user)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var confirmUrl = page.Url.Page("/Account/ConfirmEmail", pageHandler: null,
            values: new { userId = user.Id, code }, protocol: page.Request.Scheme)!;

        try
        {
            await email.SendAsync(Build(user.Email!, user.DisplayName, confirmUrl));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send confirmation e-mail to {Email}.", user.Email);
        }

        return confirmUrl;
    }

    private static EmailMessage Build(string toEmail, string? toName, string confirmUrl) => new()
    {
        ToEmail = toEmail,
        ToName = toName,
        Subject = "Potvrďte svůj e-mail – Flowlio",
        HtmlBody = $"""
            <p>Vítejte ve Flowlio,</p>
            <p>pro dokončení registrace potvrďte svou e-mailovou adresu:</p>
            <p><a href="{WebUtility.HtmlEncode(confirmUrl)}">Potvrdit e-mail</a></p>
            <p>Pokud jste účet nezakládali, tento e-mail ignorujte.</p>
            """,
        TextBody = $"Potvrďte svůj e-mail ve Flowlio: {confirmUrl}",
    };
}
