namespace Flowlio.Domain;

/// <summary>
/// The single source of truth mapping each <see cref="MemberRole"/> to the set of
/// <see cref="Permission"/>s it grants. Both the API (enforcement) and the client
/// (hiding unavailable actions) derive their behaviour from this table.
/// </summary>
public static class RolePermissions
{
    private static readonly IReadOnlySet<Permission> All = Enum.GetValues<Permission>().ToHashSet();

    private static readonly IReadOnlySet<Permission> Adult = new HashSet<Permission>
    {
        Permission.ViewFinances,
        Permission.ManageAccounts,
        Permission.ImportStatements,
        Permission.ManageAccountAccess,
        Permission.ManageCards,
    };

    private static readonly IReadOnlySet<Permission> ReadOnly = new HashSet<Permission>
    {
        Permission.ViewFinances,
    };

    /// <summary>The permissions granted to the given role.</summary>
    public static IReadOnlySet<Permission> For(MemberRole role) => role switch
    {
        MemberRole.Owner => All,
        MemberRole.Adult => Adult,
        MemberRole.Viewer => ReadOnly,
        MemberRole.Child => ReadOnly,
        _ => ReadOnly,
    };

    public static bool Has(MemberRole role, Permission permission) => For(role).Contains(permission);

    /// <summary>Whether this member's role grants the given permission.</summary>
    public static bool Can(this FamilyMember member, Permission permission) => Has(member.Role, permission);
}
