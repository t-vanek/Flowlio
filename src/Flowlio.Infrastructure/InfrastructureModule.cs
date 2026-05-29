using Flowlio.Application.Abstractions;
using Flowlio.Application.Statements;
using Flowlio.Infrastructure.Email;
using Flowlio.Infrastructure.Persistence;
using Flowlio.Infrastructure.Statements;
using Flowlio.Infrastructure.Statements.Pdf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flowlio.Infrastructure;

public static class InfrastructureModule
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            // Registers OpenIddict's entity sets in this context.
            options.UseOpenIddict();
        });

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<ICurrentFamily, CurrentFamilyResolver>();
        services.AddScoped<ICurrentSystemAccess, CurrentSystemAccess>();
        services.AddScoped<IAuditLog, AuditLog>();
        // Statement import pipeline: extraction (per format) -> bank profile (data-driven) -> mapping.
        services.AddSingleton<BankProfileRegistry>();
        services.AddSingleton<StatementMapper>();
        services.AddSingleton<IStatementReader, CsvStatementReader>();
        services.AddSingleton<IStatementReader, XlsxStatementReader>();
        services.AddSingleton<IBankDetector, BankDetector>();
        services.AddSingleton<IFormatDetector, FormatDetector>();
        // PDF import: positioned-text extraction (PdfPig) -> layout resolution -> coordinate-based table
        // parser, with a heuristic fallback for unknown layouts.
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddSingleton<PdfLayoutRegistry>();
        services.AddSingleton<PdfTableParser>();
        services.AddSingleton<PdfHeuristicParser>();
        services.AddSingleton<PdfStatementParser>();
        services.AddSingleton<IStatementImporter, StatementImporter>();

        // SMTP e-mail (invitations, account notifications). The client authorizes via an OAuth2
        // bearer token (XOAUTH2) obtained from OpenIddict through the client-credentials grant.
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.AddHttpClient(OpenIddictSmtpTokenProvider.HttpClientName);
        services.AddSingleton<ISmtpTokenProvider, OpenIddictSmtpTokenProvider>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        // Foreign-exchange rates from the Czech National Bank, refreshed in the background. Best-effort:
        // the dashboard converts to the family's base currency where a rate is available.
        services.AddHttpClient(Exchange.CnbExchangeRateClient.HttpClientName);
        services.AddSingleton<Exchange.CnbExchangeRateClient>();
        services.AddHostedService<Exchange.ExchangeRateRefresher>();

        // Open Banking (PSD2) access via the Enable Banking aggregator: a self-signed JWT (RS256) authenticates
        // the app, the consent flow links an account, and transactions are pulled into the import pipeline.
        services.Configure<Banking.EnableBankingOptions>(configuration.GetSection(Banking.EnableBankingOptions.SectionName));
        services.AddHttpClient(Banking.EnableBankingClient.HttpClientName);
        services.AddSingleton<Banking.EnableBankingTokenProvider>();
        services.AddScoped<IBankDataProvider, Banking.EnableBankingClient>();

        return services;
    }
}
