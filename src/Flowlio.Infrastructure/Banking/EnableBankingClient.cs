using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Flowlio.Application.Abstractions;
using Flowlio.Application.Statements;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flowlio.Infrastructure.Banking;

/// <summary>
/// Talks to the Enable Banking Open Banking API: lists banks, drives the consent/authorisation flow, opens
/// sessions and fetches booked transactions, mapping them to the canonical <see cref="ParsedTransaction"/>.
/// Every request carries a fresh JWT bearer minted by <see cref="EnableBankingTokenProvider"/>.
/// </summary>
internal sealed class EnableBankingClient(
    IHttpClientFactory httpClientFactory,
    EnableBankingTokenProvider tokenProvider,
    IOptions<EnableBankingOptions> options,
    ILogger<EnableBankingClient> logger) : IBankDataProvider
{
    public const string HttpClientName = "enable-banking";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly EnableBankingOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    private string BaseUrl => _options.BaseUrl.TrimEnd('/');

    public async Task<IReadOnlyList<BankAspsp>> ListBanksAsync(string country, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"{BaseUrl}/aspsps?country={Uri.EscapeDataString(country)}", null, cancellationToken);
        var payload = await ReadAsync<AspspsResponse>(response, cancellationToken);
        return payload.Aspsps
            .Select(a => new BankAspsp(a.Name ?? "", a.Country ?? country))
            .Where(a => a.Name.Length > 0)
            .ToList();
    }

    public async Task<BankAuthorizationStart> StartAuthorizationAsync(
        string aspspName, string country, string state, DateTimeOffset accessValidUntil,
        CancellationToken cancellationToken = default)
    {
        var body = new AuthRequest
        {
            Access = new AccessSpec { ValidUntil = accessValidUntil },
            Aspsp = new AspspSpec { Name = aspspName, Country = country },
            State = state,
            RedirectUrl = _options.RedirectUrl,
            PsuType = _options.PsuType,
        };

        var response = await SendAsync(HttpMethod.Post, $"{BaseUrl}/auth", body, cancellationToken);
        var payload = await ReadAsync<AuthResponse>(response, cancellationToken);
        if (string.IsNullOrEmpty(payload.Url))
            throw new InvalidOperationException("Enable Banking nevrátil autorizační URL.");

        return new BankAuthorizationStart(payload.Url, payload.AuthorizationId ?? "");
    }

    public async Task<BankAuthorizationSession> CreateSessionAsync(string code, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Post, $"{BaseUrl}/sessions", new SessionRequest { Code = code }, cancellationToken);
        var payload = await ReadAsync<SessionResponse>(response, cancellationToken);

        var accounts = payload.Accounts
            .Select(a => new BankAccountRef(a.Uid ?? "", a.AccountId?.Iban, a.Name))
            .Where(a => a.Uid.Length > 0)
            .ToList();

        return new BankAuthorizationSession(payload.SessionId ?? "", accounts, payload.Access?.ValidUntil);
    }

    public async Task<IReadOnlyList<ParsedTransaction>> FetchTransactionsAsync(
        string accountUid, DateOnly dateFrom, CancellationToken cancellationToken = default)
    {
        var result = new List<ParsedTransaction>();
        string? continuationKey = null;
        var from = dateFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Page through the bank's response; cap iterations so a misbehaving continuation token can't loop forever.
        for (var page = 0; page < 200; page++)
        {
            var url = $"{BaseUrl}/accounts/{Uri.EscapeDataString(accountUid)}/transactions?date_from={from}";
            if (continuationKey is not null)
                url += $"&continuation_key={Uri.EscapeDataString(continuationKey)}";

            var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
            var payload = await ReadAsync<TransactionsResponse>(response, cancellationToken);

            foreach (var tx in payload.Transactions)
            {
                if (Map(tx) is { } parsed)
                    result.Add(parsed);
            }

            continuationKey = payload.ContinuationKey;
            if (string.IsNullOrEmpty(continuationKey))
                break;
        }

        return result;
    }

    private static ParsedTransaction? Map(EbTransaction tx)
    {
        // Skip anything not booked (pending entries change and would churn the dedup set).
        if (!string.IsNullOrEmpty(tx.Status) && !string.Equals(tx.Status, "BOOK", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!TryParseDate(tx.BookingDate ?? tx.ValueDate ?? tx.TransactionDate, out var bookingDate))
            return null;
        if (!decimal.TryParse(tx.TransactionAmount?.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var magnitude))
            return null;

        var outgoing = string.Equals(tx.CreditDebitIndicator, "DBIT", StringComparison.OrdinalIgnoreCase);
        var amount = outgoing ? -magnitude : magnitude;

        var description = tx.RemittanceInformation is { Count: > 0 }
            ? string.Join(' ', tx.RemittanceInformation.Where(s => !string.IsNullOrWhiteSpace(s)))
            : null;

        return new ParsedTransaction
        {
            BookingDate = bookingDate,
            ValueDate = TryParseDate(tx.ValueDate, out var vd) ? vd : null,
            Amount = amount,
            Currency = tx.TransactionAmount?.Currency ?? "CZK",
            CounterpartyName = outgoing ? tx.Creditor?.Name : tx.Debtor?.Name,
            CounterpartyAccount = outgoing ? tx.CreditorAccount?.Iban : tx.DebtorAccount?.Iban,
            VariableSymbol = ExtractSymbol(description, "VS"),
            ConstantSymbol = ExtractSymbol(description, "KS"),
            SpecificSymbol = ExtractSymbol(description, "SS"),
            Description = description,
        };
    }

    private static bool TryParseDate(string? value, out DateOnly date)
    {
        date = default;
        return !string.IsNullOrWhiteSpace(value)
            && DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>Best-effort extraction of a Czech payment symbol (VS/KS/SS) from free-text remittance info.</summary>
    private static string? ExtractSymbol(string? text, string prefix)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var match = Regex.Match(text, $@"\b{prefix}\b\D{{0,3}}(\d{{1,10}})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, object? body, CancellationToken ct)
    {
        var token = await tokenProvider.GetTokenAsync(ct);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);

        var client = httpClientFactory.CreateClient(HttpClientName);
        var response = await client.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
            return response;

        var detail = await response.Content.ReadAsStringAsync(ct);
        response.Dispose();

        // A revoked/expired PSD2 consent surfaces as 401/403 — let callers re-authorise rather than retry.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new BankConsentExpiredException($"Enable Banking odmítl přístup ({(int)response.StatusCode}): {Truncate(detail)}");

        logger.LogWarning("Enable Banking request {Method} {Url} failed ({Status}).", method, url, (int)response.StatusCode);
        throw new InvalidOperationException($"Enable Banking request failed ({(int)response.StatusCode}): {Truncate(detail)}");
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        using (response)
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct)
                   ?? throw new InvalidOperationException("Enable Banking vrátil prázdnou odpověď.");
        }
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];

    // ---- Enable Banking wire shapes (snake_case via the shared JsonSerializerOptions) -------------------

    private sealed record AspspsResponse
    {
        public List<AspspInfo> Aspsps { get; init; } = [];
    }

    private sealed record AspspInfo
    {
        public string? Name { get; init; }
        public string? Country { get; init; }
    }

    private sealed record AuthRequest
    {
        public AccessSpec Access { get; init; } = new();
        public AspspSpec Aspsp { get; init; } = new();
        public string State { get; init; } = "";
        public string RedirectUrl { get; init; } = "";
        public string PsuType { get; init; } = "personal";
    }

    private sealed record AccessSpec
    {
        public DateTimeOffset ValidUntil { get; init; }
    }

    private sealed record AspspSpec
    {
        public string Name { get; init; } = "";
        public string Country { get; init; } = "";
    }

    private sealed record AuthResponse
    {
        public string? Url { get; init; }
        public string? AuthorizationId { get; init; }
    }

    private sealed record SessionRequest
    {
        public string Code { get; init; } = "";
    }

    private sealed record SessionResponse
    {
        public string? SessionId { get; init; }
        public List<SessionAccount> Accounts { get; init; } = [];
        public AccessInfo? Access { get; init; }
    }

    private sealed record AccessInfo
    {
        public DateTimeOffset? ValidUntil { get; init; }
    }

    private sealed record SessionAccount
    {
        public string? Uid { get; init; }
        public AccountIdentifier? AccountId { get; init; }
        public string? Name { get; init; }
    }

    private sealed record AccountIdentifier
    {
        public string? Iban { get; init; }
    }

    private sealed record TransactionsResponse
    {
        public List<EbTransaction> Transactions { get; init; } = [];
        public string? ContinuationKey { get; init; }
    }

    private sealed record EbTransaction
    {
        public string? EntryReference { get; init; }
        public AmountValue? TransactionAmount { get; init; }
        public string? CreditDebitIndicator { get; init; }
        public string? Status { get; init; }
        public string? BookingDate { get; init; }
        public string? ValueDate { get; init; }
        public string? TransactionDate { get; init; }
        public Party? Creditor { get; init; }
        public Party? Debtor { get; init; }
        public AccountIdentifier? CreditorAccount { get; init; }
        public AccountIdentifier? DebtorAccount { get; init; }
        public List<string>? RemittanceInformation { get; init; }
    }

    private sealed record AmountValue
    {
        public string? Currency { get; init; }
        public string? Amount { get; init; }
    }

    private sealed record Party
    {
        public string? Name { get; init; }
    }
}
