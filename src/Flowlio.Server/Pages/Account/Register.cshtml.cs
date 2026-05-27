using Flowlio.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Flowlio.Server.Pages.Account;

public class RegisterModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : PageModel
{
    [BindProperty] public string DisplayName { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl ?? "/";

    public async Task<IActionResult> OnPostAsync()
    {
        var user = new ApplicationUser
        {
            UserName = Email,
            Email = Email,
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName,
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(user, Password);
        if (!result.Succeeded)
        {
            Error = string.Join(" ", result.Errors.Select(e => e.Description));
            return Page();
        }

        await signInManager.SignInAsync(user, isPersistent: true);
        return LocalRedirect(string.IsNullOrWhiteSpace(ReturnUrl) ? "/" : ReturnUrl);
    }
}
