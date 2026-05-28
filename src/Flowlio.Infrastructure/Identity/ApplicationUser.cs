using Microsoft.AspNetCore.Identity;

namespace Flowlio.Infrastructure.Identity;

/// <summary>
/// Why an account is currently locked out, so the login UI can tell an administrator action apart from
/// an automatic system lockout. <see cref="None"/> alongside an active lockout means the system locked
/// the account itself (too many failed sign-in attempts); administrator actions set an explicit reason.
/// </summary>
public enum LockoutReason
{
    None = 0,
    AdminLock = 1,
    AdminBlock = 2,
}

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

    /// <summary>Source of the current lockout (admin lock/block) or <see cref="LockoutReason.None"/>.
    /// Combined with <c>LockoutEnd</c> it tells an admin action apart from an automatic lockout.</summary>
    public LockoutReason LockoutReason { get; set; } = LockoutReason.None;
}
