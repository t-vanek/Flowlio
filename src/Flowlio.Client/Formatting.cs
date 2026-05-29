using System.Globalization;

namespace Flowlio.Client;

/// <summary>Shared display formatting so every page renders amounts identically.</summary>
public static class Formatting
{
    private static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    /// <summary>Formats an amount with its currency. CZK renders as "Kč"; any other code is shown as-is
    /// (e.g. "199,00 EUR"). A null currency falls back to "Kč".</summary>
    public static string Money(decimal value, string? currency = null)
    {
        var number = value.ToString("N2", Cs);
        return currency is null or "CZK" or "Kč" ? $"{number} Kč" : $"{number} {currency}";
    }

    public static string Date(DateOnly value) => value.ToString("d.M.yyyy", Cs);
    public static string? Date(DateOnly? value) => value?.ToString("d.M.yyyy", Cs);
    public static string Date(DateTimeOffset value) => value.LocalDateTime.ToString("d.M.yyyy", Cs);
    public static string? Date(DateTimeOffset? value) => value?.LocalDateTime.ToString("d.M.yyyy", Cs);
}
