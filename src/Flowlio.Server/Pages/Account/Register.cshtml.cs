using Flowlio.Infrastructure.Identity;
using Flowlio.Server.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Flowlio.Server.Pages.Account;

public class RegisterModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    InvitationService invitations) : PageModel
{
    [BindProperty] public string DisplayName { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string? ReturnUrl { get; set; }
    [BindProperty] public string? Invite { get; set; }

    public string? Error { get; private set; }

    /// <summary>Name of the family the visitor was invited to, shown above the form.</summary>
    public string? InvitedToFamily { get; private set; }

    public async Task OnGetAsync(string? returnUrl = null, string? invite = null)
    {
        ReturnUrl = returnUrl ?? "/";
        Invite = invite;

        if (!string.IsNullOrWhiteSpace(invite))
        {
            var invitation = await invitations.FindPendingAsync(invite);
            if (invitation is { Status: Domain.InvitationStatus.Pending } && invitation.Member is not null)
            {
                Email = invitation.Email;
                DisplayName = invitation.Member.DisplayName;
                InvitedToFamily = invitation.Member.DisplayName;
            }
        }
    }

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

        if (!string.IsNullOrWhiteSpace(Invite))
        {
            var outcome = await invitations.AcceptAsync(Invite, user.Id);
            if (outcome is InvitationService.AcceptOutcome.Expired or InvitationService.AcceptOutcome.NotFound)
            {
                // The account was created; the invite link is just no longer valid. Surface it but let them in.
                Error = "Pozvánka už není platná, ale účet byl vytvořen.";
            }
        }

        await signInManager.SignInAsync(user, isPersistent: true);
        return LocalRedirect(string.IsNullOrWhiteSpace(ReturnUrl) ? "/" : ReturnUrl);
    }
}
