using Flowlio.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace Flowlio.Server.Auth;

/// <summary>Authorization requirement satisfied when the current user holds any system permission.</summary>
public sealed class AdminRequirement : IAuthorizationRequirement;

/// <summary>
/// Grants access to the administration surface to any user with at least one system permission,
/// resolved live from the database so role changes take effect on the next request (no token refresh).
/// Individual operations are further gated by the specific <see cref="Flowlio.Domain.SystemPermission"/>.
/// </summary>
public sealed class AdminAuthorizationHandler(ICurrentSystemAccess systemAccess)
    : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (await systemAccess.HasAnyAsync())
            context.Succeed(requirement);
    }
}
