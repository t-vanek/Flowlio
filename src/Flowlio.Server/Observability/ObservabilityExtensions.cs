using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace Flowlio.Server.Observability;

/// <summary>
/// Wires up Serilog (structured logging) and OpenTelemetry (traces + metrics). Logs, traces and
/// metrics are exported via OTLP when an endpoint is configured (env <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>
/// or config <c>OpenTelemetry:OtlpEndpoint</c>); in Development a console exporter/sink is added too.
/// </summary>
public static class ObservabilityExtensions
{
    private const string ServiceName = "flowlio-server";

    public static WebApplicationBuilder AddFlowlioObservability(this WebApplicationBuilder builder)
    {
        var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"]
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        var hasOtlp = !string.IsNullOrWhiteSpace(otlpEndpoint);
        var sentryDsn = builder.Configuration["Sentry:Dsn"];
        var hasSentry = !string.IsNullOrWhiteSpace(sentryDsn);
        var isDevelopment = builder.Environment.IsDevelopment();
        var serviceVersion = typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString() ?? "1.0.0";

        // --- Sentry: error monitoring (initialised from the "Sentry" config section; disabled if no DSN) ---
        if (hasSentry)
            builder.WebHost.UseSentry();

        // --- Serilog: the logging backend (replaces the default providers) ---
        builder.Host.UseSerilog((context, _, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("service.name", ServiceName);

            if (isDevelopment || !hasOtlp)
                configuration.WriteTo.Console();

            if (hasOtlp)
            {
                configuration.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = otlpEndpoint;
                    options.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = ServiceName,
                        ["service.version"] = serviceVersion,
                    };
                });
            }

            // Forward errors (and lower-level breadcrumbs) to Sentry, reusing the SDK that UseSentry initialised.
            if (hasSentry)
            {
                configuration.WriteTo.Sentry(options =>
                {
                    options.InitializeSdk = false;
                    options.MinimumEventLevel = LogEventLevel.Error;
                    options.MinimumBreadcrumbLevel = LogEventLevel.Information;
                });
            }
        });

        // --- OpenTelemetry: traces + metrics ---
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName, serviceVersion: serviceVersion))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRedisInstrumentation()
                    .AddSource("Npgsql")
                    .AddSource("Wolverine");

                if (isDevelopment)
                    tracing.AddConsoleExporter();
                if (hasOtlp)
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint!));
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (hasOtlp)
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint!));
            });

        return builder;
    }
}
