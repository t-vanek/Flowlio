using Flowlio.Application.Abstractions;
using Flowlio.Application.Currency;
using Microsoft.EntityFrameworkCore;

namespace Flowlio.Server.Endpoints;

internal static class CurrencyContext
{
    /// <summary>
    /// Loads the family's base currency together with a <see cref="CurrencyConverter"/> snapshot (the full
    /// ČNB rate table) — the pair every amount-converting read needs. Shared by the dashboard, the category
    /// breakdown and budgets so they all convert identically (and don't each reload the rate table inline).
    /// </summary>
    public static async Task<(string BaseCurrency, CurrencyConverter Converter)> LoadCurrencyContextAsync(
        this IAppDbContext db, Guid familyId, CancellationToken ct)
    {
        var baseCurrency = await db.Families
            .Where(f => f.Id == familyId)
            .Select(f => f.BaseCurrency)
            .FirstAsync(ct);
        var converter = new CurrencyConverter(await db.ExchangeRates.ToListAsync(ct));
        return (baseCurrency, converter);
    }
}
