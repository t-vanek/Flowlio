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

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// Align Identity's claim names with the ones OpenIddict expects.
builder.Services.Configure<IdentityOptions>(options =>
{
    options.ClaimsIdentity.UserNameClaimType = Claims.Name;
    options.ClaimsIdentity.UserIdClaimType = Claims.Subject;
    options.ClaimsIdentity.RoleClaimType = Claims.Role;
});

builder.Services.AddOpenIddict()
    .AddCore(options => options
        .UseEntityFrameworkCore()
        .UseDbContext<ApplicationDbContext>())
    .AddServer(options =>
    {
        options.SetTokenEndpointUris("connect/token");
        options.AllowPasswordFlow().AllowRefreshTokenFlow();
        options.AcceptAnonymousClients();
        options.RegisterScopes(Scopes.Email, Scopes.Profile, Scopes.OfflineAccess, "flowlio.api");

        // Development certificates; replace with managed certificates in production.
        options.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
        options.DisableAccessTokenEncryption();

        var aspNetCore = options.UseAspNetCore().EnableTokenEndpointPassthrough();
        if (builder.Environment.IsDevelopment())
            aspNetCore.DisableTransportSecurityRequirement();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
});
builder.Services.AddAuthorization();

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

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapTokenEndpoint();
app.MapApiEndpoints();
app.MapHub<NotificationsHub>("/hubs/notifications");
app.MapFallbackToFile("index.html");

await DbInitializer.InitializeAsync(app);

app.Run();
