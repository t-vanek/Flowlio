using System.Security.Claims;
using Flowlio.Application.Abstractions;
using OpenIddict.Abstractions;

namespace Flowlio.Server.Auth;

/// <summary>Reads the authenticated user's id from the current request principal.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId
    {
        get
        {
            var raw = Principal?.FindFirstValue(OpenIddictConstants.Claims.Subject)
                      ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }
}
