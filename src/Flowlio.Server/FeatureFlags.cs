namespace Flowlio.Server;

/// <summary>Configuration-driven feature toggles (the "Features" section of appsettings).</summary>
public static class FeatureFlags
{
    /// <summary>Configuration key backing <see cref="OpenBankingEnabled"/>.</summary>
    public const string OpenBankingKey = "Features:OpenBanking";

    /// <summary>
    /// Open Banking — Enable Banking (PSD2) bank connections plus the automatic background sync.
    /// Disabled by default because real operation requires a paid Enable Banking subscription. While it
    /// is off the entire surface stays hidden: the <c>/bank-connections</c> API, the public SCA callback,
    /// the background <c>BankSyncService</c>, and the SPA's "Připojení banky" navigation and page.
    /// Statement import is unaffected. Set <c>Features:OpenBanking=true</c> (config or env var) to enable.
    /// </summary>
    public static bool OpenBankingEnabled(this IConfiguration config) =>
        config.GetValue(OpenBankingKey, false);
}
