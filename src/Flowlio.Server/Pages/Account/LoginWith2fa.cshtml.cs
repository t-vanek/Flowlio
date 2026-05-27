using Flowlio.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Flowlio.Server.Pages.Account;

public class LoginWith2faModel(SignInManager<ApplicationUser> signInManager) : PageModel
{
    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public bool RememberMachine { get; set; }
    [BindProperty] public bool IsRecoveryCode { get; set; }
    [BindProperty] public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? "/";
        if (await signInManager.GetTwoFactorAuthenticationUserAsync() is null)
            return RedirectToPage("/Account/Login");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (await signInManager.GetTwoFactorAuthenticationUserAsync() is null)
            return RedirectToPage("/Account/Login");

        var code = Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var result = IsRecoveryCode
            ? await signInManager.TwoFactorRecoveryCodeSignInAsync(code)
            : await signInManager.TwoFactorAuthenticatorSignInAsync(code, isPersistent: true, rememberClient: RememberMachine);

        if (result.Succeeded)
            return LocalRedirect(string.IsNullOrWhiteSpace(ReturnUrl) ? "/" : ReturnUrl);

        Error = result.IsLockedOut ? "Účet je zamčen." : "Neplatný ověřovací kód.";
        return Page();
    }
}
