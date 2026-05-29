namespace Flowlio.Infrastructure.Banking;

/// <summary>
/// Enable Banking (Open Banking aggregator) settings, bound from the <c>EnableBanking</c> configuration
/// section. The application is authenticated with a self-signed JWT (RS256): the private key is paired with
/// an Application ID registered in the Enable Banking control panel. Keep the key out of source control —
/// supply it via user-secrets or an environment variable.
/// </summary>
public sealed class EnableBankingOptions
{
    public const string SectionName = "EnableBanking";

    public string BaseUrl { get; set; } = "https://api.enablebanking.com";

    /// <summary>Application ID from the Enable Banking control panel; used as the JWT <c>kid</c>.</summary>
    public string ApplicationId { get; set; } = "";

    /// <summary>RSA private key in PEM form, or a path to a <c>.pem</c> file containing it.</summary>
    public string PrivateKeyPem { get; set; } = "";

    /// <summary>Redirect URL registered with Enable Banking; the bank returns the user here after SCA.</summary>
    public string RedirectUrl { get; set; } = "";

    /// <summary>PSU type passed when starting authorisation; "personal" or "business".</summary>
    public string PsuType { get; set; } = "personal";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApplicationId)
        && !string.IsNullOrWhiteSpace(PrivateKeyPem)
        && !string.IsNullOrWhiteSpace(RedirectUrl);
}
