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
using Microsoft.AspNetCore.Identity;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using Wolverine;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

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
        options.AllowAuthorizationCodeFlow()
               .AllowRefreshTokenFlow()
               .RequireProofKeyForCodeExchange();

        options.RegisterScopes(Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.OfflineAccess, DbInitializer.ApiScope);

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
builder.Services.AddSignalR();
builder.Services.AddOpenApi();

builder.Host.UseWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(ImportStatementCommand).Assembly);
    // EF Core's DbContext is registered via factory delegates that Wolverine's code generation
    // cannot inline, so allow handlers to resolve those dependencies through service location.
    opts.ServiceLocationPolicy = JasperFx.CodeGeneration.Model.ServiceLocationPolicy.AlwaysAllowed;
});

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
