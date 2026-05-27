using Flowlio.Application.Abstractions;
using Flowlio.Infrastructure.Identity;
using Flowlio.Server.Auth;
using Flowlio.Server.Realtime;
using Flowlio.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Flowlio.Server.Endpoints;

/// <summary>
/// System administration (cross-family). Restricted to the <see cref="AdminRoles.Administrator"/>
/// role: create login accounts, grant/revoke admin, lock/block/restore, reset or force a password
/// change, force sign-out, soft-delete (with restore) and permanent purge. Each action notifies the
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
        admin.MapPost("/users/{userId:guid}/admin", SetAdmin);
        admin.MapPost("/users/{userId:guid}/lock", LockUser);
        admin.MapPost("/users/{userId:guid}/block", BlockUser);
        admin.MapPost("/users/{userId:guid}/restore", RestoreUser);
        admin.MapPost("/users/{userId:guid}/password", SetPassword);
        admin.MapPost("/users/{userId:guid}/force-password-change", ForcePasswordChange);
        admin.MapPost("/users/{userId:guid}/force-logout", ForceLogout);
        admin.MapDelete("/users/{userId:guid}", DeleteUser);
        admin.MapPost("/users/{userId:guid}/undelete", UndeleteUser);
        admin.MapDelete("/users/{userId:guid}/purge", PurgeUser);
    }

    private static async Task<IReadOnlyList<AdminUserDto>> GetUsers(
        UserManager<ApplicationUser> userManager, IAppDbContext db, ICurrentUser current, CancellationToken ct) =>
        await ToDtosAsync(await userManager.Users.OrderBy(u => u.Email).ToListAsync(ct), userManager, db, current, ct);

    private static async Task<IReadOnlyList<AdminUserDto>> GetDeletedUsers(
        UserManager<ApplicationUser> userManager, IAppDbContext db, ICurrentUser current, CancellationToken ct)
    {
        var deleted = await userManager.Users.IgnoreQueryFilters()
            .Where(u => u.DeletedAt != null)
            .OrderBy(u => u.Email)
            .ToListAsync(ct);
        return await ToDtosAsync(deleted, userManager, db, current, ct);
    }

    private static async Task<IResult> CreateUser(
        CreateUserRequest request, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager, CancellationToken ct)
    {
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
            if (!await roleManager.RoleExistsAsync(AdminRoles.Administrator))
                await roleManager.CreateAsync(new IdentityRole<Guid>(AdminRoles.Administrator));
            await userManager.AddToRoleAsync(user, AdminRoles.Administrator);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> SetAdmin(
        Guid userId, SetUserAdminRequest request,
        UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager, ICurrentUser current,
        IHubContext<NotificationsHub> hub, AccountNotifier notifier, CancellationToken ct)
    {
        if (userId == current.UserId && !request.IsAdmin)
            return Results.BadRequest("Vlastní administrátorská práva nelze odebrat.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        if (!await roleManager.RoleExistsAsync(AdminRoles.Administrator))
            await roleManager.CreateAsync(new IdentityRole<Guid>(AdminRoles.Administrator));

        var inRole = await userManager.IsInRoleAsync(user, AdminRoles.Administrator);
        if (request.IsAdmin && !inRole)
            await userManager.AddToRoleAsync(user, AdminRoles.Administrator);
        else if (!request.IsAdmin && inRole)
            await userManager.RemoveFromRoleAsync(user, AdminRoles.Administrator);

        await hub.NotifyUserAsync(userId, ct);
        await notifier.NotifyAsync(user, "Změna oprávnění – Flowlio",
            request.IsAdmin ? "Byla vám udělena role administrátora." : "Byla vám odebrána role administrátora.",
            "info", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> LockUser(
        Guid userId, LockUserRequest request, UserManager<ApplicationUser> userManager, ICurrentUser current,
        AccountNotifier notifier, CancellationToken ct)
    {
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
        return Results.NoContent();
    }

    private static async Task<IResult> BlockUser(
        Guid userId, UserManager<ApplicationUser> userManager, ICurrentUser current, AccountNotifier notifier, CancellationToken ct)
    {
        if (userId == current.UserId)
            return Results.BadRequest("Vlastní účet nelze zablokovat.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await notifier.NotifyAsync(user, "Účet zablokován – Flowlio",
            "Váš účet byl zablokován administrátorem.", "error", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RestoreUser(
        Guid userId, UserManager<ApplicationUser> userManager, AccountNotifier notifier, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        await notifier.NotifyAsync(user, "Účet odemčen – Flowlio", "Váš účet byl odemčen.", "success", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetPassword(
        Guid userId, AdminSetPasswordRequest request,
        UserManager<ApplicationUser> userManager, IOpenIddictTokenManager tokens, AccountNotifier notifier, CancellationToken ct)
    {
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
        return Results.NoContent();
    }

    private static async Task<IResult> ForcePasswordChange(
        Guid userId, UserManager<ApplicationUser> userManager, AccountNotifier notifier, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        user.MustChangePassword = true;
        await userManager.UpdateAsync(user);
        await notifier.NotifyAsync(user, "Vyžadována změna hesla – Flowlio",
            "Při příštím přihlášení budete vyzváni ke změně hesla.", "warning", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ForceLogout(
        Guid userId, UserManager<ApplicationUser> userManager, IOpenIddictTokenManager tokens, ICurrentUser current, CancellationToken ct)
    {
        if (userId == current.UserId)
            return Results.BadRequest("Vlastní relaci nelze ukončit zde.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        await userManager.UpdateSecurityStampAsync(user);
        await RevokeTokensAsync(tokens, userId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteUser(
        Guid userId, UserManager<ApplicationUser> userManager, IAppDbContext db, IOpenIddictTokenManager tokens,
        ICurrentUser current, AccountNotifier notifier, CancellationToken ct)
    {
        if (userId == current.UserId)
            return Results.BadRequest("Vlastní účet nelze smazat.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        // Soft delete: hide and block the account, suspend its family memberships and revoke tokens.
        // The profiles keep their user link so a restore can reactivate them.
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
        return Results.NoContent();
    }

    private static async Task<IResult> UndeleteUser(
        Guid userId, UserManager<ApplicationUser> userManager, IAppDbContext db, AccountNotifier notifier, CancellationToken ct)
    {
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
        return Results.NoContent();
    }

    private static async Task<IResult> PurgeUser(
        Guid userId, UserManager<ApplicationUser> userManager, IAppDbContext db, IOpenIddictTokenManager tokens,
        ICurrentUser current, CancellationToken ct)
    {
        if (userId == current.UserId)
            return Results.BadRequest("Vlastní účet nelze odstranit.");

        var user = await userManager.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Results.NotFound();

        // Detach the login from any family profiles so no dangling user id remains.
        var members = await db.FamilyMembers.Where(m => m.UserId == userId).ToListAsync(ct);
        foreach (var member in members)
        {
            member.UserId = null;
            member.IsActive = false;
        }
        await db.SaveChangesAsync(ct);

        await RevokeTokensAsync(tokens, userId, ct);

        var result = await userManager.DeleteAsync(user);
        return result.Succeeded
            ? Results.NoContent()
            : Results.BadRequest(string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    private static async Task<IReadOnlyList<AdminUserDto>> ToDtosAsync(
        List<ApplicationUser> users, UserManager<ApplicationUser> userManager, IAppDbContext db, ICurrentUser current, CancellationToken ct)
    {
        var adminIds = (await userManager.GetUsersInRoleAsync(AdminRoles.Administrator)).Select(u => u.Id).ToHashSet();

        var ids = users.Select(u => u.Id).ToList();
        var memberships = await db.FamilyMembers
            .Where(m => m.UserId != null && ids.Contains(m.UserId!.Value))
            .Select(m => new { UserId = m.UserId!.Value, FamilyName = m.Family!.Name })
            .ToListAsync(ct);
        var familiesByUser = memberships
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.FamilyName).Distinct().ToList());

        var now = DateTimeOffset.UtcNow;
        return users.Select(u => new AdminUserDto
        {
            Id = u.Id,
            Email = u.Email,
            DisplayName = u.DisplayName,
            IsAdmin = adminIds.Contains(u.Id),
            IsLockedOut = u.LockoutEnd is { } end && end > now,
            LockoutEndUtc = u.LockoutEnd,
            MustChangePassword = u.MustChangePassword,
            IsCurrentUser = u.Id == current.UserId,
            CreatedAt = u.CreatedAt,
            DeletedAtUtc = u.DeletedAt,
            Families = familiesByUser.TryGetValue(u.Id, out var fams) ? fams : [],
        }).ToList();
    }

    private static async Task RevokeTokensAsync(IOpenIddictTokenManager tokens, Guid userId, CancellationToken ct)
    {
        await foreach (var token in tokens.FindBySubjectAsync(userId.ToString(), ct))
            await tokens.TryRevokeAsync(token, ct);
    }
}
