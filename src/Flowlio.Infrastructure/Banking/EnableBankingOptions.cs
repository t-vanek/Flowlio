namespace Flowlio.Infrastructure.Banking;

/// <summary>
/// Instance-level Enable Banking settings, bound from the <c>EnableBanking</c> configuration section. The
/// per-user Application ID and private key are NOT here — each user brings their own (stored encrypted in the
/// database). Only deployment-wide values live here: the API base URL, the shared redirect/callback URL every
/// user registers in their Enable Banking application, and the PSU type.
/// </summary>
public sealed class EnableBankingOptions
{
    public const string SectionName = "EnableBanking";

    public string BaseUrl { get; set; } = "https://api.enablebanking.com";

    /// <summary>The callback URL every user must register as the redirect URL in their Enable Banking
    /// application; the bank returns the user here after SCA.</summary>
    public string RedirectUrl { get; set; } = "";

    /// <summary>PSU type passed when starting authorisation; "personal" or "business".</summary>
    public string PsuType { get; set; } = "personal";
}
