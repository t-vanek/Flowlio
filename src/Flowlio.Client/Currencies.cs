namespace Flowlio.Client;

/// <summary>
/// ISO 4217 currency codes offered in Flowlio's currency pickers: CZK (the usual base) followed by the
/// currencies published on the ČNB daily exchange-rate list, so any imported foreign amount has a code
/// to select and conversion to the base currency is supported.
/// </summary>
public static class Currencies
{
    public static readonly IReadOnlyList<string> Codes =
    [
        "CZK", "EUR", "USD", "GBP", "CHF", "PLN", "HUF",
        "AUD", "BGN", "BRL", "CAD", "CNY", "DKK", "HKD", "IDR", "ILS",
        "INR", "ISK", "JPY", "KRW", "MXN", "MYR", "NOK", "NZD", "PHP",
        "RON", "SEK", "SGD", "THB", "TRY", "ZAR",
    ];
}
