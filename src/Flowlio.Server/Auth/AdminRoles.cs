using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Flowlio.Server.Auth;

/// <summary>System-wide (cross-family) authorization role and helpers.</summary>
public static class AdminRoles
{
    /// <summary>ASP.NET Identity role granting access to system administration (all user accounts).</summary>
    public const string Administrator = "Administrator";

    /// <summary>Authorization policy name requiring the <see cref="Administrator"/> role.</summary>
    public const string AdminPolicy = "admin";

    public static bool IsSystemAdmin(this ClaimsPrincipal user) =>
        user.HasClaim(Claims.Role, Administrator) || user.IsInRole(Administrator);
}
