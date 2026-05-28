using System.Text;
using Flowlio.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Flowlio.Server.Pages.Account;

[AllowAnonymous]
public class ConfirmEmailModel(UserManager<ApplicationUser> userManager) : PageModel
{
    public bool Confirmed { get; private set; }

    /// <summary>Where the confirmation success links to: the login page, returning the user to the
    /// (skippable) 2FA setup once they sign in.</summary>
    public string LoginUrl { get; private set; } =
        "/Account/Login?returnUrl=" + Uri.EscapeDataString("/Account/TwoFactor?returnUrl=/");

    public async Task<IActionResult> OnGetAsync(string? userId, string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
            return Page();

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return Page();

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return Page();
        }

        var result = await userManager.ConfirmEmailAsync(user, token);
        Confirmed = result.Succeeded;
        return Page();
    }
}
