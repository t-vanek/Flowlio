using Flowlio.Application;
using Flowlio.Application.Abstractions;
using Flowlio.Application.Statements;
using Flowlio.Infrastructure;
using Flowlio.Infrastructure.Identity;
using Flowlio.Infrastructure.Persistence;
using Flowlio.Server;
using Flowlio.Server.Auth;
using Flowlio.Server.Endpoints;
using Flowlio.Server.Realtime;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using JasperFx.Resources;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using StackExchange.Redis;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");
var rabbitConnectionString = builder.Configuration.GetConnectionString("RabbitMq")
    ?? throw new InvalidOperationException("Connection string 'RabbitMq' is not configured.");

var redis = ConnectionMultiplexer.Connect(redisConnectionString);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

builder.Services.AddHttpContextAccessor();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<InvitationService>();

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
        options.SignIn.RequireConfirmedAccount = false;
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

// Bearer (API) is the default; the interactive authorize endpoint challenges the Identity cookie explicitly.
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("api", policy =>
    {
        policy.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
});

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

app.MapControllers();
app.MapRazorPages();
app.MapApiEndpoints();
app.MapHub<NotificationsHub>("/hubs/notifications");
app.MapFallbackToFile("index.html");

await DbInitializer.InitializeAsync(app);

app.Run();
