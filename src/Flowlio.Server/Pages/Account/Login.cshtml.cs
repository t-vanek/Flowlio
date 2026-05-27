using Flowlio.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Flowlio.Server.Pages.Account;

public class LoginModel(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl ?? "/";

    public async Task<IActionResult> OnPostAsync()
    {
        var result = await signInManager.PasswordSignInAsync(Email, Password, isPersistent: true, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            // Hard block: an admin set a 2FA deadline that has passed and the user
            // still hasn't enrolled. Sign the cookie back out so the OIDC flow can't
            // proceed; the admin must lift the requirement or the user must
            // contact them. The MustChangePassword + initial 2FA enrolment paths
            // (which happen *before* a deadline) are not affected here.
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

        Error = result.IsLockedOut
            ? "Účet je dočasně zablokován kvůli příliš mnoha pokusům."
            : "Neplatný e-mail nebo heslo.";
        return Page();
    }
}
