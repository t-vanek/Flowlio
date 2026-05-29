using Flowlio.Domain;
using Flowlio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flowlio.Infrastructure.Exchange;

/// <summary>
/// Keeps the <see cref="ExchangeRate"/> table fresh from ČNB. On startup and then twice a day it fetches the
/// last few fixing days (covering weekends/holidays and a first run) and upserts them. Entirely best-effort:
/// if the network is unavailable the dashboard simply reports amounts whose rate is missing.
/// </summary>
internal sealed class ExchangeRateRefresher(
    IServiceScopeFactory scopeFactory,
    CnbExchangeRateClient client,
    ILogger<ExchangeRateRefresher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshAsync(stoppingToken);
            try
            {
                await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // Fetch the last few days so a weekend/holiday or a cold start still yields the latest rates.
            for (var back = 0; back < 5; back++)
            {
                if (await client.FetchAsync(today.AddDays(-back), ct) is { } result && result.Rates.Count > 0)
                    await UpsertAsync(db, result.Date, result.Rates, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Exchange-rate refresh failed.");
        }
    }

    private static async Task UpsertAsync(
        ApplicationDbContext db, DateOnly date, IReadOnlyList<(string Code, decimal CzkPerUnit)> rates, CancellationToken ct)
    {
        var codes = rates.Select(r => r.Code).ToList();
        var existing = await db.ExchangeRates
            .Where(e => e.Date == date && codes.Contains(e.Currency))
            .ToDictionaryAsync(e => e.Currency, ct);

        foreach (var (code, czkPerUnit) in rates)
        {
            if (existing.TryGetValue(code, out var row))
                row.CzkPerUnit = czkPerUnit;
            else
                db.ExchangeRates.Add(new ExchangeRate { Currency = code, Date = date, CzkPerUnit = czkPerUnit });
        }

        await db.SaveChangesAsync(ct);
    }
}
