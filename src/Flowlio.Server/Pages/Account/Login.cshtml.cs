using Flowlio.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Flowlio.Server.Pages.Account;

public class LoginModel(SignInManager<ApplicationUser> signInManager) : PageModel
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
            return LocalRedirect(string.IsNullOrWhiteSpace(ReturnUrl) ? "/" : ReturnUrl);

        if (result.RequiresTwoFactor)
            return RedirectToPage("/Account/LoginWith2fa", new { returnUrl = ReturnUrl });

        Error = result.IsLockedOut
            ? "Účet je dočasně zablokován kvůli příliš mnoha pokusům."
            : "Neplatný e-mail nebo heslo.";
        return Page();
    }
}
