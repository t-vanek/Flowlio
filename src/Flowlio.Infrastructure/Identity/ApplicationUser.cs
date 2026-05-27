using Microsoft.AspNetCore.Identity;

namespace Flowlio.Infrastructure.Identity;

/// <summary>Authenticated user. A user can belong to one or more families via FamilyMember.</summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When true the user must set a new password (enforced at the authorization endpoint) before continuing.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Soft-delete marker. When set the account is hidden everywhere and cannot sign in, but can be restored or purged.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>
    /// Deadline by which the user must enrol in 2FA. When set and 2FA is still
    /// disabled, the authorization endpoint redirects to the setup page with an
    /// urgent banner; after the deadline passes login is blocked entirely.
    /// </summary>
    public DateTimeOffset? Require2faBy { get; set; }
}
