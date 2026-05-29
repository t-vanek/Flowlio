using Flowlio.Application;
using Flowlio.Application.Abstractions;
using Flowlio.Application.Statements;
using Flowlio.Infrastructure;
using Flowlio.Infrastructure.Identity;
using Flowlio.Infrastructure.Persistence;
using Flowlio.Server;
using Flowlio.Server.Auth;
using Flowlio.Server.Endpoints;
using Flowlio.Server.Observability;
using Flowlio.Server.Realtime;
using Serilog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using JasperFx.Resources;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using StackExchange.Redis;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);

builder.AddFlowlioObservability();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");
var rabbitConnectionString = builder.Configuration.GetConnectionString("RabbitMq")
    ?? throw new InvalidOperationException("Connection string 'RabbitMq' is not configured.");

var accessTokenLifetime = TimeSpan.FromMinutes(builder.Configuration.GetValue("Auth:AccessTokenMinutes", 15));
var refreshTokenLifetime = TimeSpan.FromDays(builder.Configuration.GetValue("Auth:RefreshTokenDays", 14));

var redis = ConnectionMultiplexer.Connect(redisConnectionString);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

builder.Services.AddHttpContextAccessor();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<InvitationService>();
builder.Services.AddScoped<AccountNotifier>();

// Encrypts per-user Open Banking private keys at rest (Data Protection key ring is persisted to Redis below).
builder.Services.AddSingleton<Flowlio.Application.Abstractions.ISecretProtector, Flowlio.Server.Banking.DataProtectionSecretProtector>();

// Background "automatic import": periodically pulls new transactions for active Open Banking connections.
builder.Services.AddHostedService<Flowlio.Server.Banking.BankSyncService>();

// Redis-backed distributed cache (read-through views) and shared Data Protection key ring so
// auth/antiforgery cookies stay valid across restarts and multiple instances.
builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redisConnectionString);
builder.Services.AddDataProtection()
    .PersistKeysToStackExchangeRedis(redis, "flowlio:dataprotection-keys");

builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        // Self-service sign-ups must confirm their e-mail before they can sign in. Invited accounts are
        // pre-confirmed (the invite itself proves e-mail ownership) and the demo account is seeded confirmed.
        options.SignIn.RequireConfirmedEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Align Identity's claim names with the ones OpenIddict expects.
builder.Services.Configure<IdentityOptions>(options =>
{
    options.ClaimsIdentity.UserNameClaimType = Claims.Name;
    options.ClaimsIdentity.UserIdClaimType = Claims.Subject;
    options.ClaimsIdentity.RoleClaimType = Claims.Role;
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.Cookie.SameSite = SameSiteMode.Lax;
    // The app is served over HTTPS (UseHttpsRedirection), so the auth cookie must never travel in clear.
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services.AddOpenIddict()
    .AddCore(options => options
        .UseEntityFrameworkCore()
        .UseDbContext<ApplicationDbContext>())
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("connect/authorize")
               .SetTokenEndpointUris("connect/token")
               .SetEndSessionEndpointUris("connect/logout")
               .SetUserInfoEndpointUris("connect/userinfo");

        // Authorization-code flow with PKCE (mandatory) for the public SPA, plus refresh tokens.
        // Client-credentials lets the server-side mailer mint its own access token for SMTP (XOAUTH2).
        options.AllowAuthorizationCodeFlow()
               .AllowRefreshTokenFlow()
               .AllowClientCredentialsFlow()
               .RequireProofKeyForCodeExchange();

        options.RegisterScopes(Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.OfflineAccess, DbInitializer.ApiScope, DbInitializer.SmtpScope);

        // Short-lived access tokens keep a lock/delete from lingering; refresh tokens are validated
        // against the user (lockout) on every renewal, so revoked access propagates within the window.
        options.SetAccessTokenLifetime(accessTokenLifetime);
        options.SetRefreshTokenLifetime(refreshTokenLifetime);

        // Development certificates; replace with managed certificates in production.
        options.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
        options.DisableAccessTokenEncryption();

        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough()
               .EnableEndSessionEndpointPassthrough()
               .EnableUserInfoEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

// The default scheme stays the Identity application cookie (set by AddIdentity above), so the
// interactive Razor Pages (/Account/*) and the /connect/authorize login work with the sign-in
// cookie and challenge to /Account/Login. The API, the SignalR hub and the userinfo endpoint each
// pin the OpenIddict bearer scheme explicitly (the "api"/admin policies and
// [Authorize(AuthenticationSchemes = ...)]), so they keep validating bearer tokens regardless.

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("api", policy =>
    {
        policy.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });

    options.AddPolicy(AdminRoles.AdminPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new AdminRequirement());
    });
});

// DB-backed admin check so role changes take effect immediately (no token refresh required).
builder.Services.AddScoped<IAuthorizationHandler, AdminAuthorizationHandler>();

builder.Services.AddControllers();
builder.Services.AddRazorPages();

// Redis backplane lets SignalR broadcasts reach clients connected to any server instance.
builder.Services.AddSignalR().AddStackExchangeRedis(redisConnectionString);
builder.Services.AddOpenApi();

builder.Host.UseWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(ImportStatementCommand).Assembly);
    // EF Core's DbContext is registered via factory delegates that Wolverine's code generation
    // cannot inline, so allow handlers to resolve those dependencies through service location.
    opts.ServiceLocationPolicy = JasperFx.CodeGeneration.Model.ServiceLocationPolicy.AlwaysAllowed;

    // Durable messaging: persist envelopes in PostgreSQL and enroll outgoing messages in the same
    // EF Core transaction as the business data (transactional outbox).
    opts.PersistMessagesWithPostgresql(connectionString);
    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
    opts.Policies.UseDurableInboxOnAllListeners();

    // RabbitMQ transport: statement-import completion events are published and consumed asynchronously.
    opts.UseRabbitMq(new Uri(rabbitConnectionString)).AutoProvision();
    opts.PublishMessage<StatementImported>().ToRabbitQueue("flowlio.statement-imported");
    opts.ListenToRabbitQueue("flowlio.statement-imported");
});

// Create Wolverine's message-storage tables (and RabbitMQ objects) on startup.
builder.Services.AddResourceSetupOnStartup();

var app = builder.Build();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Translate a suspended-membership signal raised deep in the request into a clean 403.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (FamilyAccessDeniedException ex)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { detail = ex.Message });
    }
    catch (DbUpdateConcurrencyException)
    {
        // Optimistic concurrency: the row changed since the client loaded it.
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { detail = "Data byla mezitím změněna jiným uživatelem. Načtěte je prosím znovu." });
    }
});

app.MapControllers();
app.MapRazorPages();
app.MapApiEndpoints();
app.MapBankConnectionCallback();
app.MapHub<NotificationsHub>("/hubs/notifications");
app.MapFallbackToFile("index.html");

await DbInitializer.InitializeAsync(app);

app.Run();
