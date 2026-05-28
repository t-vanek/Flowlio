using Flowlio.Application.Abstractions;
using Flowlio.Infrastructure.Identity;
using Flowlio.Server.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Hosting;

namespace Flowlio.Server.Pages.Account;

public class RegisterModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    InvitationService invitations,
    IEmailSender emailSender,
    IWebHostEnvironment env,
    ILogger<RegisterModel> logger) : PageModel
{
    [BindProperty] public string DisplayName { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string? ReturnUrl { get; set; }
    [BindProperty] public string? Invite { get; set; }

    public string? Error { get; private set; }

    /// <summary>Name of the family the visitor was invited to, shown above the form.</summary>
    public string? InvitedToFamily { get; private set; }

    /// <summary>Set after a self-service sign-up so the view shows the "check your e-mail" state.</summary>
    public bool AwaitingConfirmation { get; private set; }

    /// <summary>In Development the confirmation link is surfaced here so registration is testable
    /// without a running SMTP server. Always null outside Development.</summary>
    public string? DevConfirmLink { get; private set; }

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
        var accountEmail = Email;
        var hasInvite = false;
        if (!string.IsNullOrWhiteSpace(Invite))
        {
            var invitation = await invitations.FindPendingAsync(Invite);
            if (invitation is { Status: Domain.InvitationStatus.Pending, Member: not null })
            {
                accountEmail = invitation.Email;
                hasInvite = true;
            }
        }

        var user = new ApplicationUser
        {
            UserName = accountEmail,
            Email = accountEmail,
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName,
            // An invite is itself proof of e-mail ownership, so invited accounts are pre-confirmed;
            // self-service sign-ups must confirm via the e-mailed link before they can sign in.
            EmailConfirmed = hasInvite,
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

            // Offer 2FA setup right after registration as a recommended, skippable step. The setup
            // page's continue/skip links carry the returnUrl on to /connect/authorize.
            await signInManager.SignInAsync(user, isPersistent: true);
            var returnUrl = string.IsNullOrWhiteSpace(ReturnUrl) ? "/" : ReturnUrl;
            return Redirect("/Account/TwoFactor?returnUrl=" + Uri.EscapeDataString(returnUrl));
        }

        // Self sign-up: e-mail a confirmation link and wait. The account cannot sign in until confirmed
        // (SignIn.RequireConfirmedEmail), so we do not start a session here.
        var confirmUrl = await AccountEmails.SendConfirmationAsync(this, userManager, emailSender, logger, user);
        if (env.IsDevelopment())
            DevConfirmLink = confirmUrl;
        AwaitingConfirmation = true;
        return Page();
    }
}
