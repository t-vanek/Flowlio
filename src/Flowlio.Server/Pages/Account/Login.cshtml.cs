using Flowlio.Application.Abstractions;
using Flowlio.Infrastructure.Identity;
using Flowlio.Server.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Flowlio.Server.Pages.Account;

public class LoginModel(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IEmailSender emailSender,
    ILogger<LoginModel> logger) : PageModel
{
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }
    public string? Info { get; private set; }

    /// <summary>True when the last attempt failed because the e-mail is unconfirmed, so the view can
    /// offer to resend the confirmation link.</summary>
    public bool ShowResend { get; private set; }

    public void OnGet(string? returnUrl = null, string? reason = null)
    {
        ReturnUrl = returnUrl ?? "/";
        if (reason == "locked")
            Error = "Vaše relace byla ukončena, protože účet je uzamčen nebo zablokován. Přihlaste se prosím znovu.";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var result = await signInManager.PasswordSignInAsync(Email, Password, isPersistent: true, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            // Hard block: an admin set a 2FA deadline that has passed and the user
            // still hasn't enrolled. Sign the cookie back out so the OIDC flow can't
            // proceed; the admin must lift the requirement or the user must contact them.
            var user = await userManager.FindByNameAsync(Email);
            if (user is { Require2faBy: { } deadline } && !await userManager.GetTwoFactorEnabledAsync(user) && deadline < DateTimeOffset.UtcNow)
            {
                await signInManager.SignOutAsync();
                Error = "Lhůta pro zapnutí dvoufaktorového ověření vypršela. Kontaktujte administrátora.";
                return Page();
            }

            return LocalRedirect(string.IsNullOrWhiteSpace(ReturnUrl) ? "/" : ReturnUrl);
        }

        if (result.RequiresTwoFactor)
            return RedirectToPage("/Account/LoginWith2fa", new { returnUrl = ReturnUrl });

        if (result.IsNotAllowed)
        {
            // The usual cause is an unconfirmed e-mail (SignIn.RequireConfirmedEmail).
            ShowResend = true;
            Error = "Než se přihlásíte, potvrďte prosím svůj e-mail. Odkaz jsme vám poslali při registraci.";
            return Page();
        }

        if (result.IsLockedOut)
        {
            var locked = await userManager.FindByNameAsync(Email);
            Error = AccountLockout.SignInMessage(locked is null ? null : await userManager.GetLockoutEndDateAsync(locked));
            return Page();
        }

        Error = "Neplatný e-mail nebo heslo.";
        return Page();
    }

    public async Task<IActionResult> OnPostResendAsync()
    {
        var user = await userManager.FindByEmailAsync(Email);
        if (user is not null && !await userManager.IsEmailConfirmedAsync(user))
            await AccountEmails.SendConfirmationAsync(this, userManager, emailSender, logger, user);

        // Generic response whether or not the account exists / is already confirmed, to avoid enumeration.
        Info = "Pokud účet existuje a není potvrzený, poslali jsme nový potvrzovací odkaz.";
        return Page();
    }
}
