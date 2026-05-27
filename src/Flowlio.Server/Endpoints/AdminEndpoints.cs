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
/// change, force sign-out (token revocation) and delete.
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(AdminRoles.AdminPolicy);
        admin.MapGet("/users", GetUsers);
        admin.MapPost("/users", CreateUser);
        admin.MapPost("/users/{userId:guid}/admin", SetAdmin);
        admin.MapPost("/users/{userId:guid}/lock", LockUser);
        admin.MapPost("/users/{userId:guid}/block", BlockUser);
        admin.MapPost("/users/{userId:guid}/restore", RestoreUser);
        admin.MapPost("/users/{userId:guid}/password", SetPassword);
        admin.MapPost("/users/{userId:guid}/force-password-change", ForcePasswordChange);
        admin.MapPost("/users/{userId:guid}/force-logout", ForceLogout);
        admin.MapDelete("/users/{userId:guid}", DeleteUser);
    }

    private static async Task<IReadOnlyList<AdminUserDto>> GetUsers(
        UserManager<ApplicationUser> userManager, IAppDbContext db, ICurrentUser current, CancellationToken ct)
    {
        var users = await userManager.Users.OrderBy(u => u.Email).ToListAsync(ct);
        var adminIds = (await userManager.GetUsersInRoleAsync(AdminRoles.Administrator)).Select(u => u.Id).ToHashSet();

        var memberships = await db.FamilyMembers
            .Where(m => m.UserId != null)
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
            Families = familiesByUser.TryGetValue(u.Id, out var fams) ? fams : [],
        }).ToList();
    }

    private static async Task<IResult> CreateUser(
        CreateUserRequest request, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return Results.BadRequest("E-mail je povinný.");
        if (string.IsNullOrWhiteSpace(request.Password))
            return Results.BadRequest("Heslo je povinné.");

        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
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
        IHubContext<NotificationsHub> hub, CancellationToken ct)
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
        return Results.NoContent();
    }

    private static async Task<IResult> LockUser(
        Guid userId, LockUserRequest request, UserManager<ApplicationUser> userManager, ICurrentUser current)
    {
        if (userId == current.UserId)
            return Results.BadRequest("Vlastní účet nelze zamknout.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        var minutes = Math.Clamp(request.Minutes, 1, 60 * 24 * 365);
        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(minutes));
        return Results.NoContent();
    }

    private static async Task<IResult> BlockUser(
        Guid userId, UserManager<ApplicationUser> userManager, ICurrentUser current)
    {
        if (userId == current.UserId)
            return Results.BadRequest("Vlastní účet nelze zablokovat.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        return Results.NoContent();
    }

    private static async Task<IResult> RestoreUser(Guid userId, UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        return Results.NoContent();
    }

    private static async Task<IResult> SetPassword(
        Guid userId, AdminSetPasswordRequest request,
        UserManager<ApplicationUser> userManager, IOpenIddictTokenManager tokens, CancellationToken ct)
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

        // Existing sessions used the old password: revoke their tokens so they must re-authenticate.
        await RevokeTokensAsync(tokens, userId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ForcePasswordChange(Guid userId, UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        user.MustChangePassword = true;
        await userManager.UpdateAsync(user);
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

        // Invalidate the Identity cookie and revoke OpenIddict tokens so refresh fails on next renewal.
        await userManager.UpdateSecurityStampAsync(user);
        await RevokeTokensAsync(tokens, userId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteUser(
        Guid userId, UserManager<ApplicationUser> userManager, IAppDbContext db, ICurrentUser current, CancellationToken ct)
    {
        if (userId == current.UserId)
            return Results.BadRequest("Vlastní účet nelze smazat.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.NotFound();

        // Detach the login from any family profiles so no dangling user id remains; the profiles
        // become inactive managed records that an owner can reassign or remove.
        var members = await db.FamilyMembers.Where(m => m.UserId == userId).ToListAsync(ct);
        foreach (var member in members)
        {
            member.UserId = null;
            member.IsActive = false;
        }
        await db.SaveChangesAsync(ct);

        var result = await userManager.DeleteAsync(user);
        return result.Succeeded
            ? Results.NoContent()
            : Results.BadRequest(string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    private static async Task RevokeTokensAsync(IOpenIddictTokenManager tokens, Guid userId, CancellationToken ct)
    {
        await foreach (var token in tokens.FindBySubjectAsync(userId.ToString(), ct))
            await tokens.TryRevokeAsync(token, ct);
    }
}
