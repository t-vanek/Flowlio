namespace Flowlio.Domain;

/// <summary>
/// The <em>default</em> mapping of each <see cref="MemberRole"/> to the set of
/// <see cref="Permission"/>s it grants. New families are seeded from this table
/// (see <see cref="FamilyRolePermission.CreateDefaults"/>); afterwards each family's owner may
/// customise the permissions of the non-owner roles, so runtime enforcement reads the
/// per-family overrides rather than this table. The <see cref="MemberRole.Owner"/> always holds
/// every permission and is never editable.
/// </summary>
public static class RolePermissions
{
    private static readonly IReadOnlySet<Permission> All = Enum.GetValues<Permission>().ToHashSet();

    /// <summary>Roles whose permissions a family owner may customise (everything except Owner).</summary>
    public static readonly IReadOnlyList<MemberRole> EditableRoles =
        [MemberRole.Adult, MemberRole.Viewer, MemberRole.Child];

    private static readonly IReadOnlySet<Permission> Adult = new HashSet<Permission>
    {
        Permission.ViewFinances,
        Permission.ManageAccounts,
        Permission.ImportStatements,
        Permission.ManageTransactions,
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
