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
        // For an invited registration the account must use the e-mail the invite was issued to.
        // Never trust the posted value here, or the invite could be redeemed against an
        // attacker-chosen address, seizing the invited member's seat.
        var email = Email;
        var hasInvite = false;
        if (!string.IsNullOrWhiteSpace(Invite))
        {
            var invitation = await invitations.FindPendingAsync(Invite);
            if (invitation is { Status: Domain.InvitationStatus.Pending, Member: not null })
            {
                email = invitation.Email;
                hasInvite = true;
            }
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName,
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(user, Password);
        if (!result.Succeeded)
        {
            Error = string.Join(" ", result.Errors.Select(e => e.Description));
            return Page();
        }

        if (hasInvite)
        {
            var outcome = await invitations.AcceptAsync(Invite!, user.Id);
            if (outcome is not InvitationService.AcceptOutcome.Accepted)
            {
                // The account was created; the invite link is just no longer applicable. Surface it but let them in.
                Error = "Pozvánka už není platná, ale účet byl vytvořen.";
            }
        }

        await signInManager.SignInAsync(user, isPersistent: true);

        // Offer 2FA setup right after registration as a recommended, skippable step. The setup page's
        // continue/skip links carry the returnUrl on to /connect/authorize so the OIDC flow resumes.
        var returnUrl = string.IsNullOrWhiteSpace(ReturnUrl) ? "/" : ReturnUrl;
        return Redirect("/Account/TwoFactor?returnUrl=" + Uri.EscapeDataString(returnUrl));
    }
}
