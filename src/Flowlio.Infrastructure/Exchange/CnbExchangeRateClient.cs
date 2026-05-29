using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Flowlio.Infrastructure.Exchange;

/// <summary>Fetches the ČNB daily FX list over HTTP and parses it. Network failures are swallowed (returns
/// null) so the app degrades gracefully — conversion then reports "rate missing" rather than crashing.</summary>
internal sealed class CnbExchangeRateClient(IHttpClientFactory httpClientFactory, ILogger<CnbExchangeRateClient> logger)
{
    public const string HttpClientName = "cnb-fx";

    private const string BaseUrl =
        "https://www.cnb.cz/cs/financni-trhy/devizovy-trh/kurzy-devizoveho-trhu/kurzy-devizoveho-trhu/denni_kurz.txt";

    public async Task<(DateOnly Date, IReadOnlyList<(string Code, decimal CzkPerUnit)> Rates)?> FetchAsync(
        DateOnly date, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var url = $"{BaseUrl}?date={date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}";
            var content = await client.GetStringAsync(url, ct);
            return CnbRateParser.Parse(content);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "ČNB exchange-rate fetch failed for {Date}.", date);
            return null;
        }
    }
}
