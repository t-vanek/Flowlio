using Flowlio.Application.Abstractions;
using Flowlio.Infrastructure.Identity;
using Flowlio.Server.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Flowlio.Server.Pages.Account;

public class LoginModel(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IEmailSender emailSender,
    IOptions<IdentityOptions> identityOptions,
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

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? "/";

        // A session ended by the authorize endpoint (locked/blocked mid-session) drops a short-lived
        // flash cookie, because the OIDC re-challenge would otherwise strip any reason from the URL.
        if (Request.Cookies.TryGetValue(AccountLockout.SessionEndedCookie, out var kindStr)
            && Enum.TryParse<AccountLockout.Kind>(kindStr, out var kind) && kind != AccountLockout.Kind.None)
        {
            Error = AccountLockout.SessionEndedMessage(kind);
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Consume the session-ended flash: from here on, messages come from the attempt itself.
        Response.Cookies.Delete(AccountLockout.SessionEndedCookie);

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

            await ClearStaleLockoutReasonAsync(user);
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
            var end = locked is null ? null : await userManager.GetLockoutEndDateAsync(locked);
            var kind = AccountLockout.Resolve(true, locked?.LockoutReason ?? LockoutReason.None, end);
            Error = AccountLockout.SignInMessage(kind, end);
            return Page();
        }

        // Wrong password (account not locked). Clear a stale admin lock reason so a later automatic
        // lockout is attributed to the system, and warn once the account is close to being locked.
        var failedUser = await userManager.FindByNameAsync(Email);
        await ClearStaleLockoutReasonAsync(failedUser);
        Error = await WrongPasswordMessageAsync(failedUser);
        return Page();
    }

    private async Task<string> WrongPasswordMessageAsync(ApplicationUser? user)
    {
        const string generic = "Neplatný e-mail nebo heslo.";
        if (user is null || !await userManager.GetLockoutEnabledAsync(user))
            return generic;

        // Only surface the countdown over the last couple of attempts: it warns before an imminent
        // lockout without leaking account existence on a single typo.
        var remaining = identityOptions.Value.Lockout.MaxFailedAccessAttempts - await userManager.GetAccessFailedCountAsync(user);
        return remaining switch
        {
            1 => $"{generic} Zbývá poslední pokus, než se účet dočasně uzamkne.",
            2 => $"{generic} Zbývají 2 pokusy, než se účet dočasně uzamkne.",
            _ => generic,
        };
    }

    private async Task ClearStaleLockoutReasonAsync(ApplicationUser? user)
    {
        if (user is { LockoutReason: not LockoutReason.None } && !await userManager.IsLockedOutAsync(user))
        {
            user.LockoutReason = LockoutReason.None;
            await userManager.UpdateAsync(user);
        }
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
