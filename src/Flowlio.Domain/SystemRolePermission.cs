using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>
/// Grants one <see cref="SystemPermission"/> to a system role (an ASP.NET Identity role, referenced
/// by <see cref="RoleId"/>). The built-in <see cref="SystemRoles.Administrator"/> role is never stored
/// here — it always holds every permission.
/// </summary>
public class SystemRolePermission : Entity
{
    public Guid RoleId { get; set; }
    public SystemPermission Permission { get; set; }
}
