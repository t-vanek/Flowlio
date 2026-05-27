using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>
/// A single (role, permission) grant scoped to one family. The set of rows for a given
/// (<see cref="FamilyId"/>, <see cref="Role"/>) defines what that role may do in that family.
/// <see cref="MemberRole.Owner"/> is intentionally never stored here — owners always hold every
/// permission.
/// </summary>
public class FamilyRolePermission : Entity
{
    public Guid FamilyId { get; set; }
    public Family? Family { get; set; }

    public MemberRole Role { get; set; }
    public Permission Permission { get; set; }

    /// <summary>The default grants for a freshly created family, derived from <see cref="RolePermissions"/>.</summary>
    public static IEnumerable<FamilyRolePermission> CreateDefaults(Guid familyId)
    {
        foreach (var role in RolePermissions.EditableRoles)
            foreach (var permission in RolePermissions.For(role))
                yield return new FamilyRolePermission { FamilyId = familyId, Role = role, Permission = permission };
    }
}
