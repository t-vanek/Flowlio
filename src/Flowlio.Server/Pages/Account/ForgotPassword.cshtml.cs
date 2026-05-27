using System.Net;
using Flowlio.Application.Abstractions;
using Flowlio.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Flowlio.Server.Pages.Account;

public class ForgotPasswordModel(UserManager<ApplicationUser> userManager, IEmailSender email) : PageModel
{
    [BindProperty] public string Email { get; set; } = "";

    /// <summary>Shown after submit; deliberately generic to avoid revealing whether an account exists.</summary>
    public bool Sent { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Sent = true;

        var user = await userManager.FindByEmailAsync(Email);
        if (user is not null)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var link = Url.Page("/Account/ResetPassword", pageHandler: null,
                values: new { email = Email, token }, protocol: Request.Scheme)!;

            await email.SendAsync(new EmailMessage
            {
                ToEmail = Email,
                ToName = user.DisplayName,
                Subject = "Obnova hesla – Flowlio",
                HtmlBody = $"""
                    <p>Pro nastavení nového hesla otevřete <a href="{WebUtility.HtmlEncode(link)}">tento odkaz</a>.</p>
                    <p>Pokud jste o obnovu nežádali, tento e-mail ignorujte.</p>
                    """,
                TextBody = $"Obnova hesla: {link}",
            });
        }

        return Page();
    }
}
