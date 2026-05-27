using Flowlio.Infrastructure.Identity;
using Flowlio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Flowlio.Server;

/// <summary>
/// Applies pending migrations and seeds the OIDC client, API scope and a demo family account
/// on startup (development convenience).
/// </summary>
public static class DbInitializer
{
    public const string SpaClientId = "flowlio-spa";
    public const string ApiScope = "flowlio.api";

    public static async Task InitializeAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        await SeedOpenIddictAsync(sp);
        await SeedDemoUserAsync(sp);
    }

    private static async Task SeedOpenIddictAsync(IServiceProvider sp)
    {
        var config = sp.GetRequiredService<IConfiguration>();

        var redirectUris = config.GetSection("Spa:RedirectUris").Get<string[]>()
            ?? ["https://localhost:5443/authentication/login-callback"];
        var postLogoutUris = config.GetSection("Spa:PostLogoutRedirectUris").Get<string[]>()
            ?? ["https://localhost:5443/authentication/logout-callback"];

        var scopes = sp.GetRequiredService<IOpenIddictScopeManager>();
        if (await scopes.FindByNameAsync(ApiScope) is null)
        {
            await scopes.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = ApiScope,
                DisplayName = "Flowlio API",
                Resources = { "flowlio-api" },
            });
        }

        var applications = sp.GetRequiredService<IOpenIddictApplicationManager>();
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = SpaClientId,
            ClientType = ClientTypes.Public,
            DisplayName = "Flowlio SPA",
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Prefixes.Scope + ApiScope,
            },
            Requirements =
            {
                Requirements.Features.ProofKeyForCodeExchange,
            },
        };

        foreach (var uri in redirectUris)
            descriptor.RedirectUris.Add(new Uri(uri));
        foreach (var uri in postLogoutUris)
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri));

        var existing = await applications.FindByClientIdAsync(SpaClientId);
        if (existing is null)
            await applications.CreateAsync(descriptor);
        else
            await applications.UpdateAsync(existing, descriptor);
    }

    private static async Task SeedDemoUserAsync(IServiceProvider sp)
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var email = config["Seed:DemoEmail"];
        var password = config["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(email) is null)
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = "Rodina Flowlio",
            };
            await users.CreateAsync(user, password);
        }
    }
}
