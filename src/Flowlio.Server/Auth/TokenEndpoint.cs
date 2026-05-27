using System.Security.Claims;
using Flowlio.Infrastructure.Identity;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Flowlio.Server.Auth;

/// <summary>OAuth2 token endpoint backing the SPA login. Supports password and refresh-token grants.</summary>
public static class TokenEndpoint
{
    public static void MapTokenEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/connect/token", Handle);
    }

    private static async Task<IResult> Handle(
        HttpContext context,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        ApplicationUser? user;

        if (request.IsPasswordGrantType())
        {
            user = await users.FindByNameAsync(request.Username!)
                   ?? await users.FindByEmailAsync(request.Username!);

            if (user is null || !(await signIn.CheckPasswordSignInAsync(user, request.Password!, lockoutOnFailure: true)).Succeeded)
                return Forbid("Neplatné přihlašovací údaje.");
        }
        else if (request.IsRefreshTokenGrantType())
        {
            var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var userId = result.Principal?.GetClaim(Claims.Subject);
            user = userId is null ? null : await users.FindByIdAsync(userId);

            if (user is null)
                return Forbid("Obnovovací token už není platný.");
        }
        else
        {
            return Forbid("Nepodporovaný typ grantu.");
        }

        if (user is null)
            return Forbid("Uživatele se nepodařilo ověřit.");

        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationDefaults.AuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id.ToString());
        identity.SetClaim(Claims.Email, user.Email);
        identity.SetClaim(Claims.Name, user.UserName);
        identity.SetClaim(Claims.PreferredUsername, user.DisplayName ?? user.UserName);

        identity.SetScopes(Scopes.Profile, Scopes.Email, Scopes.OfflineAccess, "flowlio.api");
        identity.SetDestinations(_ => [Destinations.AccessToken]);

        return Results.SignIn(new ClaimsPrincipal(identity), null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IResult Forbid(string description) => Results.Forbid(
        authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
        properties: new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        }));

    private static class TokenValidationDefaults
    {
        public const string AuthenticationType = "Flowlio";
    }
}
