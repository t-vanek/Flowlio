using System.Globalization;
using Flowlio.Domain;

namespace Flowlio.Client;

/// <summary>Display helpers for money, currencies and banks.</summary>
public static class Format
{
    private static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    public static string Money(decimal value, Currency currency) =>
        value.ToString("N2", Cs) + " " + Symbol(currency);

    public static string Money(decimal value) => value.ToString("N2", Cs) + " Kč";

    public static string Symbol(Currency currency) => currency switch
    {
        Currency.CZK => "Kč",
        Currency.EUR => "€",
        Currency.USD => "$",
        Currency.GBP => "£",
        Currency.PLN => "zł",
        Currency.CHF => "CHF",
        Currency.HUF => "Ft",
        _ => currency.ToString(),
    };

    public static string Bank(BankProvider bank) => bank switch
    {
        BankProvider.Csob => "ČSOB",
        BankProvider.KomercniBanka => "Komerční banka",
        BankProvider.CeskaSporitelna => "Česká spořitelna",
        BankProvider.Fio => "Fio banka",
        BankProvider.AirBank => "Air Bank",
        BankProvider.Revolut => "Revolut",
        _ => "Jiná banka",
    };

    public static string Role(MemberRole role) => role switch
    {
        MemberRole.Owner => "Vlastník",
        MemberRole.Adult => "Dospělý",
        MemberRole.Viewer => "Pozorovatel",
        _ => role.ToString(),
    };
}
