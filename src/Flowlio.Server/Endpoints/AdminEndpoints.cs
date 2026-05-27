using Flowlio.Application.Abstractions;
using Flowlio.Domain;
using Flowlio.Infrastructure.Identity;
using Flowlio.Server.Auth;
using Flowlio.Server.Realtime;
using Flowlio.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static Flowlio.Server.Auth.MemberAuthorization;

namespace Flowlio.Server.Endpoints;

/// <summary>
/// System administration (cross-family). The group is open to any user with a system permission;
/// each operation is gated by its specific <see cref="SystemPermission"/>, audited, and notifies the
/// affected user (live toast + e-mail).
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(AdminRoles.AdminPolicy);
        admin.MapGet("/users", GetUsers);
        admin.MapGet("/users/deleted", GetDeletedUsers);
        admin.MapPost("/users", CreateUser);
        admin.MapPut("/users/{userId:guid}/roles", SetUserRoles);
        admin.MapPost("/users/{userId:guid}/lock", LockUser);
        admin.MapPost("/users/{userId:guid}/block", BlockUser);
        admin.MapPost("/users/{userId:guid}/restore", RestoreUser);
        admin.MapPost("/users/{userId:guid}/password", SetPassword);
        admin.MapPost("/users/{userId:guid}/force-password-change", ForcePasswordChange);
        admin.MapPost("/users/{userId:guid}/disable-2fa", DisableTwoFactor);
        admin.MapPost("/users/{userId:guid}/force-logout", ForceLogout);
        admin.MapDelete("/users/{userId:guid}", DeleteUser);
        admin.MapPost("/users/{userId:guid}/undelete", UndeleteUser);
        admin.MapDelete("/users/{userId:guid}/purge", PurgeUser);
    }

    private static async Task<IResult> GetUsers(
        UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager,
        IAppDbContext db, ICurrentUser current, ICurrentSystemAccess sys, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.ViewUsers, ct))
            return Forbidden();
        var users = await userManager.Users.OrderBy(u => u.Email).ToListAsync(ct);
        return Results.Ok(await ToDtosAsync(users, userManager, roleManager, db, current, ct));
    }

    private static async Task<IResult> GetDeletedUsers(
        UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager,
        IAppDbContext db, ICurrentUser current, ICurrentSystemAccess sys, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.DeleteUsers, ct))
            return Forbidden();
        var deleted = await userManager.Users.IgnoreQueryFilters()
            .Where(u => u.DeletedAt != null)
            .OrderBy(u => u.Email)
            .ToListAsync(ct);
        return Results.Ok(await ToDtosAsync(deleted, userManager, roleManager, db, current, ct));
    }

    private static async Task<IResult> CreateUser(
        CreateUserRequest request, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager,
        ICurrentSystemAccess sys, IAuditLog audit, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.CreateUsers, ct))
            return Forbidden();
        if (string.IsNullOrWhiteSpace(request.Email))
            return Results.BadRequest("E-mail je povinný.");
        if (string.IsNullOrWhiteSpace(request.Password))
            return Results.BadRequest("Heslo je povinné.");

        var email = request.Email.Trim();
        var clash = await userManager.Users.IgnoreQueryFilters()
            .AnyAsync(u => u.DeletedAt != null && u.NormalizedEmail == email.ToUpperInvariant(), ct);
        if (clash)
            return Results.BadRequest("Účet s tímto e-mailem je ve smazaných. Nejprve jej obnovte, nebo trvale odstraňte.");

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
            MustChangePassword = request.MustChangePassword,
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return Results.BadRequest(string.Join(" ", result.Errors.Select(e => e.Description)));

        if (request.IsAdmin)
        {
            if (!await roleManager.RoleExistsAsync(SystemRoles.Administrator))
                await roleManager.CreateAsync(new IdentityRole<Guid>(SystemRoles.Administrator));
            await userManager.AddToRoleAsync(user, SystemRoles.Administrator);
        }

        await audit.RecordAsync("user.create", "User", user.Id.ToString(), user.Email,
            details: request.IsAdmin ? "Vytvořen účet (administrátor)" : "Vytvořen účet", cancellationToken: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetUserRoles(
        Guid userId, SetUserRolesRequest request,
        UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager,
        ICurrentUser current, ICurrentSystemAccess sys, IHubContext<NotificationsHub> hub, AccountNotifier notifier,
        IAuditLog audit, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.ManageUserRoles, ct))
            return Forbidden();

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        var existingRoleNames = await roleManager.Roles.Select(r => r.Name!).ToListAsync(ct);
        var requested = (request.RoleNames ?? [])
            .Where(r => existingRoleNames.Contains(r))
            .ToHashSet(StringComparer.Ordinal);

        var currentRoles = (await userManager.GetRolesAsync(user)).ToHashSet(StringComparer.Ordinal);

        if (userId == current.UserId
            && currentRoles.Contains(SystemRoles.Administrator)
            && !requested.Contains(SystemRoles.Administrator))
            return Results.BadRequest("Vlastní roli administrátora nelze odebrat.");

        var toAdd = requested.Except(currentRoles).ToList();
        var toRemove = currentRoles.Except(requested).ToList();
        if (toAdd.Count > 0)
            await userManager.AddToRolesAsync(user, toAdd);
        if (toRemove.Count > 0)
            await userManager.RemoveFromRolesAsync(user, toRemove);

        await hub.NotifyUserAsync(userId, ct);
        await notifier.NotifyAsync(user, "Změna rolí – Flowlio", "Byly vám upraveny systémové role účtu.", "info", ct);
        await audit.RecordAsync("user.roles", "User", userId.ToString(), user.Email,
            details: $"Role: {(requested.Count > 0 ? string.Join(", ", requested) : "žádné")}", cancellationToken: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> LockUser(
        Guid userId, LockUserRequest request, UserManager<ApplicationUser> userManager, ICurrentUser current,
        ICurrentSystemAccess sys, AccountNotifier notifier, IAuditLog audit, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.ManageUserLockout, ct))
            return Forbidden();
        if (userId == current.UserId)
            return Results.BadRequest("Vlastní účet nelze zamknout.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        var minutes = Math.Clamp(request.Minutes, 1, 60 * 24 * 365);
        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(minutes));
        await notifier.NotifyAsync(user, "Účet dočasně zamčen – Flowlio",
            $"Váš účet byl dočasně zamčen na {minutes} minut.", "warning", ct);
        await audit.RecordAsync("user.lock", "User", userId.ToString(), user.Email,
            details: $"Zamčeno na {minutes} min", cancellationToken: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> BlockUser(
        Guid userId, UserManager<ApplicationUser> userManager, ICurrentUser current,
        ICurrentSystemAccess sys, AccountNotifier notifier, IAuditLog audit, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.ManageUserLockout, ct))
            return Forbidden();
        if (userId == current.UserId)
            return Results.BadRequest("Vlastní účet nelze zablokovat.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await notifier.NotifyAsync(user, "Účet zablokován – Flowlio",
            "Váš účet byl zablokován administrátorem.", "error", ct);
        await audit.RecordAsync("user.block", "User", userId.ToString(), user.Email, details: "Účet zablokován", cancellationToken: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RestoreUser(
        Guid userId, UserManager<ApplicationUser> userManager, ICurrentSystemAccess sys, AccountNotifier notifier,
        IAuditLog audit, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.ManageUserLockout, ct))
            return Forbidden();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        await notifier.NotifyAsync(user, "Účet odemčen – Flowlio", "Váš účet byl odemčen.", "success", ct);
        await audit.RecordAsync("user.unlock", "User", userId.ToString(), user.Email, details: "Účet odemčen", cancellationToken: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetPassword(
        Guid userId, AdminSetPasswordRequest request,
        UserManager<ApplicationUser> userManager, IOpenIddictTokenManager tokens, ICurrentSystemAccess sys,
        AccountNotifier notifier, IAuditLog audit, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.ManageUserPasswords, ct))
            return Forbidden();
        if (string.IsNullOrWhiteSpace(request.NewPassword))
            return Results.BadRequest("Nové heslo je povinné.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
            return Results.BadRequest(string.Join(" ", result.Errors.Select(e => e.Description)));

        if (request.MustChangePassword)
        {
            user.MustChangePassword = true;
            await userManager.UpdateAsync(user);
        }

        await RevokeTokensAsync(tokens, userId, ct);
        await notifier.NotifyAsync(user, "Heslo bylo změněno – Flowlio",
            "Administrátor vám nastavil nové heslo. Při příštím přihlášení budete vyzváni k volbě vlastního.", "warning", ct);
        await audit.RecordAsync("user.password-reset", "User", userId.ToString(), user.Email, details: "Heslo nastaveno administrátorem", cancellationToken: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ForcePasswordChange(
        Guid userId, UserManager<ApplicationUser> userManager, ICurrentSystemAccess sys, AccountNotifier notifier,
        IAuditLog audit, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.ManageUserPasswords, ct))
            return Forbidden();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        user.MustChangePassword = true;
        await userManager.UpdateAsync(user);
        await notifier.NotifyAsync(user, "Vyžadována změna hesla – Flowlio",
            "Při příštím přihlášení budete vyzváni ke změně hesla.", "warning", ct);
        await audit.RecordAsync("user.force-password-change", "User", userId.ToString(), user.Email, details: "Vynucena změna hesla", cancellationToken: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DisableTwoFactor(
        Guid userId, UserManager<ApplicationUser> userManager, ICurrentSystemAccess sys, AccountNotifier notifier,
        IAuditLog audit, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.ManageUserPasswords, ct))
            return Forbidden();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);
        await notifier.NotifyAsync(user, "Dvoufaktorové ověření vypnuto – Flowlio",
            "Administrátor vypnul dvoufaktorové ověření na vašem účtu.", "warning", ct);
        await audit.RecordAsync("user.disable-2fa", "User", userId.ToString(), user.Email, details: "Vypnuto 2FA", cancellationToken: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ForceLogout(
        Guid userId, UserManager<ApplicationUser> userManager, IOpenIddictTokenManager tokens, ICurrentUser current,
        ICurrentSystemAccess sys, IAuditLog audit, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.ForceUserLogout, ct))
            return Forbidden();
        if (userId == current.UserId)
            return Results.BadRequest("Vlastní relaci nelze ukončit zde.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        await userManager.UpdateSecurityStampAsync(user);
        await RevokeTokensAsync(tokens, userId, ct);
        await audit.RecordAsync("user.force-logout", "User", userId.ToString(), user.Email, details: "Vynuceno odhlášení", cancellationToken: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteUser(
        Guid userId, UserManager<ApplicationUser> userManager, IAppDbContext db, IOpenIddictTokenManager tokens,
        ICurrentUser current, ICurrentSystemAccess sys, AccountNotifier notifier, IAuditLog audit, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.DeleteUsers, ct))
            return Forbidden();
        if (userId == current.UserId)
            return Results.BadRequest("Vlastní účet nelze smazat.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        // Soft delete: hide and block the account, suspend its family memberships and revoke tokens.
        user.DeletedAt = DateTimeOffset.UtcNow;
        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await userManager.UpdateAsync(user);

        var members = await db.FamilyMembers.Where(m => m.UserId == userId).ToListAsync(ct);
        foreach (var member in members)
            member.IsActive = false;
        await db.SaveChangesAsync(ct);

        await RevokeTokensAsync(tokens, userId, ct);
        await notifier.NotifyAsync(user, "Účet deaktivován – Flowlio",
            "Váš účet byl deaktivován administrátorem.", "error", ct);
        await audit.RecordAsync("user.delete", "User", userId.ToString(), user.Email, details: "Účet smazán (soft delete)", cancellationToken: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UndeleteUser(
        Guid userId, UserManager<ApplicationUser> userManager, IAppDbContext db, ICurrentSystemAccess sys,
        AccountNotifier notifier, IAuditLog audit, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.DeleteUsers, ct))
            return Forbidden();
        var user = await userManager.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Results.NotFound();

        user.DeletedAt = null;
        await userManager.UpdateAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);

        var members = await db.FamilyMembers.Where(m => m.UserId == userId).ToListAsync(ct);
        foreach (var member in members)
            member.IsActive = true;
        await db.SaveChangesAsync(ct);

        await notifier.NotifyAsync(user, "Účet obnoven – Flowlio", "Váš účet byl obnoven.", "success", ct);
        await audit.RecordAsync("user.undelete", "User", userId.ToString(), user.Email, details: "Účet obnoven", cancellationToken: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> PurgeUser(
        Guid userId, UserManager<ApplicationUser> userManager, IAppDbContext db, IOpenIddictTokenManager tokens,
        ICurrentUser current, ICurrentSystemAccess sys, IAuditLog audit, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.DeleteUsers, ct))
            return Forbidden();
        if (userId == current.UserId)
            return Results.BadRequest("Vlastní účet nelze odstranit.");

        var user = await userManager.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Results.NotFound();

        var members = await db.FamilyMembers.Where(m => m.UserId == userId).ToListAsync(ct);
        foreach (var member in members)
        {
            member.UserId = null;
            member.IsActive = false;
        }
        await db.SaveChangesAsync(ct);

        await RevokeTokensAsync(tokens, userId, ct);

        var email = user.Email;
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
            return Results.BadRequest(string.Join(" ", result.Errors.Select(e => e.Description)));

        await audit.RecordAsync("user.purge", "User", userId.ToString(), email, details: "Účet trvale odstraněn", cancellationToken: ct);
        return Results.NoContent();
    }

    private static async Task<IReadOnlyList<AdminUserDto>> ToDtosAsync(
        List<ApplicationUser> users, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager,
        IAppDbContext db, ICurrentUser current, CancellationToken ct)
    {
        var rolesByUser = new Dictionary<Guid, List<string>>();
        var roleNames = await roleManager.Roles.Select(r => r.Name!).ToListAsync(ct);
        foreach (var roleName in roleNames)
            foreach (var member in await userManager.GetUsersInRoleAsync(roleName))
                (rolesByUser.TryGetValue(member.Id, out var list) ? list : rolesByUser[member.Id] = []).Add(roleName);

        var ids = users.Select(u => u.Id).ToList();
        var memberships = await db.FamilyMembers
            .Where(m => m.UserId != null && ids.Contains(m.UserId!.Value))
            .Select(m => new { UserId = m.UserId!.Value, FamilyName = m.Family!.Name })
            .ToListAsync(ct);
        var familiesByUser = memberships
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.FamilyName).Distinct().ToList());

        var now = DateTimeOffset.UtcNow;
        return users.Select(u =>
        {
            var roles = rolesByUser.TryGetValue(u.Id, out var rs) ? rs : [];
            return new AdminUserDto
            {
                Id = u.Id,
                Email = u.Email,
                DisplayName = u.DisplayName,
                IsAdmin = roles.Contains(SystemRoles.Administrator),
                IsLockedOut = u.LockoutEnd is { } end && end > now,
                LockoutEndUtc = u.LockoutEnd,
                MustChangePassword = u.MustChangePassword,
                TwoFactorEnabled = u.TwoFactorEnabled,
                IsCurrentUser = u.Id == current.UserId,
                CreatedAt = u.CreatedAt,
                DeletedAtUtc = u.DeletedAt,
                Families = familiesByUser.TryGetValue(u.Id, out var fams) ? fams : [],
                Roles = roles,
            };
        }).ToList();
    }

    private static async Task RevokeTokensAsync(IOpenIddictTokenManager tokens, Guid userId, CancellationToken ct)
    {
        await foreach (var token in tokens.FindBySubjectAsync(userId.ToString(), ct))
            await tokens.TryRevokeAsync(token, ct);
    }
}
