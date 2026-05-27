namespace Flowlio.Domain;

/// <summary>
/// A discrete capability within a family. Roles are defined as sets of these permissions
/// (see <see cref="RolePermissions"/>), and the API authorizes actions by permission rather
/// than by inspecting the role directly.
/// </summary>
public enum Permission
{
    /// <summary>View dashboards, accounts, transactions, categories and cards.</summary>
    ViewFinances = 0,
    /// <summary>Create and edit bank accounts.</summary>
    ManageAccounts = 1,
    /// <summary>Import bank statements.</summary>
    ImportStatements = 2,
    /// <summary>Grant or revoke per-account access (disponents / shared viewers).</summary>
    ManageAccountAccess = 3,
    /// <summary>Create, edit or remove payment cards.</summary>
    ManageCards = 4,
    /// <summary>Add, remove and invite family members.</summary>
    ManageMembers = 5,
}
