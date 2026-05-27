using Flowlio.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Flowlio.Server.Pages.Account;

public class ResetPasswordModel(UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Token { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string ConfirmPassword { get; set; } = "";

    public string? Error { get; private set; }
    public bool Done { get; private set; }

    public void OnGet(string? email = null, string? token = null)
    {
        Email = email ?? "";
        Token = token ?? "";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Password != ConfirmPassword)
        {
            Error = "Hesla se neshodují.";
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Email);
        if (user is null)
        {
            Error = "Neplatný odkaz pro obnovu.";
            return Page();
        }

        var result = await userManager.ResetPasswordAsync(user, Token, Password);
        if (!result.Succeeded)
        {
            Error = string.Join(" ", result.Errors.Select(e => e.Description));
            return Page();
        }

        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            await userManager.UpdateAsync(user);
        }

        Done = true;
        return Page();
    }
}
