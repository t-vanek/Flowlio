using System.Text;
using Flowlio.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;

namespace Flowlio.Server.Pages.Account;

[Authorize]
public class TwoFactorModel(UserManager<ApplicationUser> userManager) : PageModel
{
    public bool Is2faEnabled { get; private set; }
    public string? SharedKey { get; private set; }
    public string? AuthenticatorUri { get; private set; }
    public string? AuthenticatorQrDataUri { get; private set; }

    /// <summary>Admin-set deadline by which the user must enrol in 2FA, if any.</summary>
    public DateTimeOffset? Require2faBy { get; private set; }

    /// <summary>
    /// Where to send the user back to once 2FA setup is complete. Honoured by the
    /// `?returnUrl=...` query string so the OIDC `/connect/authorize` flow can
    /// force 2FA enrolment and then resume issuing tokens.
    /// </summary>
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    [BindProperty] public string VerificationCode { get; set; } = "";

    public string[]? RecoveryCodes { get; private set; }
    public string? Status { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return RedirectToPage("/Account/Login");

        Is2faEnabled = await userManager.GetTwoFactorEnabledAsync(user);
        Require2faBy = user.Require2faBy;
        if (!Is2faEnabled)
            await LoadSharedKeyAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostEnableAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return RedirectToPage("/Account/Login");

        var code = VerificationCode.Replace(" ", string.Empty).Replace("-", string.Empty);
        var valid = await userManager.VerifyTwoFactorTokenAsync(
            user, userManager.Options.Tokens.AuthenticatorTokenProvider, code);
        if (!valid)
        {
            Error = "Neplatný ověřovací kód.";
            await LoadSharedKeyAsync(user);
            return Page();
        }

        await userManager.SetTwoFactorEnabledAsync(user, true);
        Is2faEnabled = true;
        RecoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToArray();
        Status = "Dvoufaktorové ověření bylo zapnuto. Uložte si záchranné kódy na bezpečné místo.";
        return Page();
    }

    /// <summary>Streams the recovery codes as a plain-text download (no caching).</summary>
    public async Task<IActionResult> OnPostDownloadRecoveryAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return RedirectToPage("/Account/Login");

        if (!await userManager.GetTwoFactorEnabledAsync(user))
            return RedirectToPage();

        var codes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToArray();
        var body = new StringBuilder()
            .AppendLine("Flowlio – záchranné kódy 2FA")
            .AppendLine($"Vygenerováno: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC")
            .AppendLine("Každý kód lze použít právě jednou.")
            .AppendLine()
            .AppendJoin(Environment.NewLine, codes)
            .AppendLine()
            .ToString();
        return File(Encoding.UTF8.GetBytes(body), "text/plain; charset=utf-8", "flowlio-recovery-codes.txt");
    }

    public async Task<IActionResult> OnPostDisableAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return RedirectToPage("/Account/Login");

        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);
        Is2faEnabled = false;
        await LoadSharedKeyAsync(user);
        Status = "Dvoufaktorové ověření bylo vypnuto.";
        return Page();
    }

    public async Task<IActionResult> OnPostRegenerateAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return RedirectToPage("/Account/Login");

        Is2faEnabled = await userManager.GetTwoFactorEnabledAsync(user);
        RecoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToArray();
        Status = "Byly vygenerovány nové záchranné kódy. Předchozí přestaly platit.";
        return Page();
    }

    private async Task LoadSharedKeyAsync(ApplicationUser user)
    {
        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        SharedKey = FormatKey(key!);
        var email = await userManager.GetEmailAsync(user);
        AuthenticatorUri =
            $"otpauth://totp/Flowlio:{Uri.EscapeDataString(email ?? user.Id.ToString())}?secret={key}&issuer=Flowlio&digits=6";
        AuthenticatorQrDataUri = GenerateQrDataUri(AuthenticatorUri);
    }

    private static string GenerateQrDataUri(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule: 6);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    private static string FormatKey(string key)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
            sb.Append(key.AsSpan(i, Math.Min(4, key.Length - i))).Append(' ');
        return sb.ToString().Trim().ToUpperInvariant();
    }
}
