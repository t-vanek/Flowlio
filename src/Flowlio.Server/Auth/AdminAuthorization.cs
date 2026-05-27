using System.Security.Claims;
using Flowlio.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Flowlio.Server.Auth;

/// <summary>Authorization requirement satisfied when the current user holds the admin role in the database.</summary>
public sealed class AdminRequirement : IAuthorizationRequirement;

/// <summary>
/// Checks the system-admin role against the database (not the access-token claims) so that granting
/// or revoking admin in the UI takes effect on the very next request, without waiting for a token
/// refresh or a server restart.
/// </summary>
public sealed class AdminAuthorizationHandler(UserManager<ApplicationUser> userManager)
    : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        var subject = context.User.FindFirstValue(Claims.Subject)
                      ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(subject, out var userId))
            return;

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is not null && await userManager.IsInRoleAsync(user, AdminRoles.Administrator))
            context.Succeed(requirement);
    }
}
