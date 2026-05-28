using Flowlio.Infrastructure.Statements;
using Xunit;

namespace Flowlio.Tests;

public class MerchantNameTests
{
    [Fact]
    public void Extracts_merchant_and_strips_marker_and_date()
    {
        var result = MerchantName.FromDescription("Platba kartou 12.05.2024 ALBERT 1234 PRAHA");

        Assert.NotNull(result);
        Assert.Contains("ALBERT", result!);
        Assert.DoesNotContain("kartou", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("12.05.2024", result);
    }

    [Fact]
    public void Strips_masked_card_amount_and_currency()
    {
        var result = MerchantName.FromDescription("Nákup kartou NETFLIX.COM 123456******1234 250,00 CZK");

        Assert.NotNull(result);
        Assert.Contains("NETFLIX", result!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("CZK", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Returns_null_for_non_card_description()
    {
        // A plain transfer/fee label has no card marker, so we must not invent a counterparty for it.
        Assert.Null(MerchantName.FromDescription("Převod na účet Nájemné"));
    }

    [Fact]
    public void Returns_null_for_blank()
    {
        Assert.Null(MerchantName.FromDescription(null));
        Assert.Null(MerchantName.FromDescription("   "));
    }
}
