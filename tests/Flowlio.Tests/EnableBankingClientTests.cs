using System.Net;
using System.Security.Cryptography;
using System.Text;
using Flowlio.Application.Abstractions;
using Flowlio.Infrastructure.Banking;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flowlio.Tests;

public class EnableBankingClientTests
{
    [Fact]
    public async Task FetchTransactions_maps_sign_dates_symbols_and_skips_pending()
    {
        const string json = """
        {
          "transactions": [
            {
              "transaction_amount": { "currency": "CZK", "amount": "1234.50" },
              "credit_debit_indicator": "DBIT",
              "status": "BOOK",
              "booking_date": "2026-05-01",
              "value_date": "2026-05-02",
              "creditor": { "name": "Albert" },
              "creditor_account": { "iban": "CZ123" },
              "remittance_information": ["Nakup", "VS:1234 KS:0308 SS:55"]
            },
            {
              "transaction_amount": { "currency": "CZK", "amount": "45000.00" },
              "credit_debit_indicator": "CRDT",
              "status": "BOOK",
              "booking_date": "2026-05-03",
              "debtor": { "name": "Zamestnavatel" },
              "debtor_account": { "iban": "CZ999" },
              "remittance_information": ["Mzda"]
            },
            {
              "transaction_amount": { "currency": "CZK", "amount": "10.00" },
              "credit_debit_indicator": "DBIT",
              "status": "PDNG",
              "booking_date": "2026-05-04"
            }
          ],
          "continuation_key": null
        }
        """;

        var handler = new StubHandler(_ => (HttpStatusCode.OK, json));
        var result = await Client(handler).FetchTransactionsAsync(Creds(), "acc-uid", new DateOnly(2026, 1, 1));

        Assert.Equal(2, result.Count); // pending entry skipped

        var expense = result[0];
        Assert.Equal(-1234.50m, expense.Amount);
        Assert.Equal(new DateOnly(2026, 5, 1), expense.BookingDate);
        Assert.Equal(new DateOnly(2026, 5, 2), expense.ValueDate);
        Assert.Equal("Albert", expense.CounterpartyName);
        Assert.Equal("CZ123", expense.CounterpartyAccount);
        Assert.Equal("1234", expense.VariableSymbol);
        Assert.Equal("0308", expense.ConstantSymbol);
        Assert.Equal("55", expense.SpecificSymbol);

        var income = result[1];
        Assert.Equal(45000.00m, income.Amount);
        Assert.Equal("Zamestnavatel", income.CounterpartyName);
        Assert.Equal("CZ999", income.CounterpartyAccount);
    }

    [Fact]
    public async Task FetchTransactions_follows_the_continuation_key()
    {
        const string page1 = """
        { "transactions": [ { "transaction_amount": { "currency": "CZK", "amount": "1.00" }, "credit_debit_indicator": "DBIT", "status": "BOOK", "booking_date": "2026-05-01" } ], "continuation_key": "next" }
        """;
        const string page2 = """
        { "transactions": [ { "transaction_amount": { "currency": "CZK", "amount": "2.00" }, "credit_debit_indicator": "DBIT", "status": "BOOK", "booking_date": "2026-05-02" } ], "continuation_key": null }
        """;

        var handler = new StubHandler(req =>
            (HttpStatusCode.OK, req.RequestUri!.Query.Contains("continuation_key") ? page2 : page1));

        var result = await Client(handler).FetchTransactionsAsync(Creds(), "acc-uid", new DateOnly(2026, 1, 1));

        Assert.Equal(2, result.Count);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task FetchTransactions_throws_consent_expired_on_401()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.Unauthorized, "{\"error\":\"consent expired\"}"));

        await Assert.ThrowsAsync<BankConsentExpiredException>(
            () => Client(handler).FetchTransactionsAsync(Creds(), "acc-uid", new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public async Task ListBanks_maps_aspsps()
    {
        const string json = """
        { "aspsps": [ { "name": "Air Bank", "country": "CZ" }, { "name": "ČSOB", "country": "CZ" } ] }
        """;
        var handler = new StubHandler(_ => (HttpStatusCode.OK, json));

        var banks = await Client(handler).ListBanksAsync(Creds(), "CZ");

        Assert.Equal(2, banks.Count);
        Assert.Equal("Air Bank", banks[0].Name);
        Assert.Equal("CZ", banks[0].Country);
    }

    private static EnableBankingClient Client(StubHandler handler) =>
        new(new StubFactory(handler), new EnableBankingTokenProvider(),
            Options.Create(new EnableBankingOptions
            {
                BaseUrl = "https://api.enablebanking.com",
                RedirectUrl = "https://localhost/bank-connections/callback",
            }),
            NullLogger<EnableBankingClient>.Instance);

    private static BankProviderCredentials Creds()
    {
        using var rsa = RSA.Create(2048);
        return new BankProviderCredentials("app-1", rsa.ExportPkcs8PrivateKeyPem());
    }

    private sealed class StubHandler(Func<HttpRequestMessage, (HttpStatusCode Status, string Json)> responder)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var (status, json) = responder(request);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
