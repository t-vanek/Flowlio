using Microsoft.AspNetCore.Identity;

namespace Flowlio.Infrastructure.Identity;

/// <summary>Authenticated user. A user can belong to one or more families via FamilyMember.</summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When true the user must set a new password (enforced at the authorization endpoint) before continuing.</summary>
    public bool MustChangePassword { get; set; }
}
