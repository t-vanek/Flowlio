using System.Globalization;

namespace Flowlio.Client;

/// <summary>Shared display formatting so every page renders amounts identically.</summary>
public static class Formatting
{
    private static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    public static string Money(decimal value) => value.ToString("N2", Cs) + " Kč";
}
