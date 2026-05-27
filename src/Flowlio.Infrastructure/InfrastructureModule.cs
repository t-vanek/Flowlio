using Flowlio.Application.Abstractions;
using Flowlio.Application.Statements;
using Flowlio.Infrastructure.Email;
using Flowlio.Infrastructure.Persistence;
using Flowlio.Infrastructure.Statements;
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
        services.AddSingleton<PdfStatementParser>();
        services.AddSingleton<IStatementImporter, StatementImporter>();

        // SMTP e-mail (invitations, account notifications). The client authorizes via an OAuth2
        // bearer token (XOAUTH2) obtained from OpenIddict through the client-credentials grant.
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.AddHttpClient(OpenIddictSmtpTokenProvider.HttpClientName);
        services.AddSingleton<ISmtpTokenProvider, OpenIddictSmtpTokenProvider>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
