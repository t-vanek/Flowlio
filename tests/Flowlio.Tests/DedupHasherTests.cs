using Flowlio.Application.Statements;
using Xunit;

namespace Flowlio.Tests;

public class DedupHasherTests
{
    private static ParsedTransaction Tx(decimal amount, string? vs = null) => new()
    {
        BookingDate = new DateOnly(2026, 5, 1),
        Amount = amount,
        Currency = "CZK",
        VariableSymbol = vs,
        Description = "test",
    };

    [Fact]
    public void Same_transaction_produces_same_hash()
    {
        var account = Guid.NewGuid();
        Assert.Equal(DedupHasher.Compute(account, Tx(-100m)), DedupHasher.Compute(account, Tx(-100m)));
    }

    [Fact]
    public void Different_amount_produces_different_hash()
    {
        var account = Guid.NewGuid();
        Assert.NotEqual(DedupHasher.Compute(account, Tx(-100m)), DedupHasher.Compute(account, Tx(-200m)));
    }

    [Fact]
    public void Different_account_produces_different_hash()
    {
        Assert.NotEqual(DedupHasher.Compute(Guid.NewGuid(), Tx(-100m)), DedupHasher.Compute(Guid.NewGuid(), Tx(-100m)));
    }
}
