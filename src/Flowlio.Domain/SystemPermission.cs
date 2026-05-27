namespace Flowlio.Domain;

/// <summary>
/// A cross-family capability over login accounts, granted to system roles and managed by a system
/// administrator. Distinct from the family-scoped <see cref="Permission"/>.
/// </summary>
public enum SystemPermission
{
    /// <summary>View the list of all login accounts.</summary>
    ViewUsers = 0,
    /// <summary>Create new login accounts.</summary>
    CreateUsers = 1,
    /// <summary>Assign or remove a user's system roles (including administrator).</summary>
    ManageUserRoles = 2,
    /// <summary>Lock, block or unlock accounts.</summary>
    ManageUserLockout = 3,
    /// <summary>Reset passwords or require a password change.</summary>
    ManageUserPasswords = 4,
    /// <summary>Force a user to sign out (revoke their tokens).</summary>
    ForceUserLogout = 5,
    /// <summary>Soft-delete, restore or permanently purge accounts.</summary>
    DeleteUsers = 6,
    /// <summary>Create, edit and delete system roles and their permissions.</summary>
    ManageSystemRoles = 7,
    /// <summary>View the audit log of administrative actions.</summary>
    ViewAuditLog = 8,
}

/// <summary>Well-known system role names.</summary>
public static class SystemRoles
{
    /// <summary>The built-in super administrator role: always holds every system permission and is immutable.</summary>
    public const string Administrator = "Administrator";
}
