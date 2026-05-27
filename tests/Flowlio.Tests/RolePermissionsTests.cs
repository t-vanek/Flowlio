using Flowlio.Domain;

namespace Flowlio.Tests;

public class RolePermissionsTests
{
    [Fact]
    public void Owner_has_every_permission()
    {
        foreach (var permission in Enum.GetValues<Permission>())
            Assert.True(RolePermissions.Has(MemberRole.Owner, permission));
    }

    [Fact]
    public void Adult_can_manage_finances_but_not_members()
    {
        Assert.True(RolePermissions.Has(MemberRole.Adult, Permission.ViewFinances));
        Assert.True(RolePermissions.Has(MemberRole.Adult, Permission.ManageAccounts));
        Assert.True(RolePermissions.Has(MemberRole.Adult, Permission.ImportStatements));
        Assert.True(RolePermissions.Has(MemberRole.Adult, Permission.ManageAccountAccess));
        Assert.True(RolePermissions.Has(MemberRole.Adult, Permission.ManageCards));
        Assert.False(RolePermissions.Has(MemberRole.Adult, Permission.ManageMembers));
        Assert.False(RolePermissions.Has(MemberRole.Adult, Permission.ManageRoles));
        Assert.False(RolePermissions.Has(MemberRole.Adult, Permission.ManageFamily));
    }

    [Fact]
    public void Default_grants_cover_only_editable_non_owner_roles()
    {
        var defaults = FamilyRolePermission.CreateDefaults(Guid.NewGuid()).ToList();

        Assert.DoesNotContain(defaults, r => r.Role == MemberRole.Owner);
        Assert.All(defaults, r => Assert.Contains(r.Role, RolePermissions.EditableRoles));
        Assert.Contains(defaults, r => r.Role == MemberRole.Adult && r.Permission == Permission.ManageAccounts);
    }

    [Theory]
    [InlineData(MemberRole.Viewer)]
    [InlineData(MemberRole.Child)]
    public void Read_only_roles_can_only_view(MemberRole role)
    {
        Assert.True(RolePermissions.Has(role, Permission.ViewFinances));

        foreach (var permission in Enum.GetValues<Permission>())
        {
            if (permission == Permission.ViewFinances)
                continue;
            Assert.False(RolePermissions.Has(role, permission));
        }
    }

    [Fact]
    public void Can_extension_matches_role_table()
    {
        var owner = new FamilyMember { DisplayName = "Owner", Role = MemberRole.Owner };
        var viewer = new FamilyMember { DisplayName = "Viewer", Role = MemberRole.Viewer };

        Assert.True(owner.Can(Permission.ManageMembers));
        Assert.False(viewer.Can(Permission.ManageMembers));
        Assert.True(viewer.Can(Permission.ViewFinances));
    }
}
