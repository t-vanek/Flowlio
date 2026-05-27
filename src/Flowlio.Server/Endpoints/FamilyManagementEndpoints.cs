using Flowlio.Application.Abstractions;
using Flowlio.Domain;
using Flowlio.Server.Realtime;
using Flowlio.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using static Flowlio.Server.Auth.MemberAuthorization;

namespace Flowlio.Server.Endpoints;

/// <summary>Family-level administration: rename, transfer ownership and delete the whole family.</summary>
public static class FamilyManagementEndpoints
{
    public static void MapFamilyManagementEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/family", GetFamily);
        api.MapPut("/family", UpdateFamily);
        api.MapPost("/family/transfer-ownership", TransferOwnership);
        api.MapDelete("/family", DeleteFamily);
    }

    private static async Task<IResult> GetFamily(IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        var fam = await db.Families.FirstOrDefaultAsync(f => f.Id == me.FamilyId, ct);
        if (fam is null)
            return Results.NotFound();

        var memberCount = await db.FamilyMembers.CountAsync(m => m.FamilyId == fam.Id, ct);
        var owner = await db.FamilyMembers
            .FirstOrDefaultAsync(m => m.FamilyId == fam.Id && m.Role == MemberRole.Owner, ct);

        return Results.Ok(new FamilyDto
        {
            Id = fam.Id,
            Name = fam.Name,
            BaseCurrency = fam.BaseCurrency,
            MemberCount = memberCount,
            OwnerMemberId = owner?.Id,
            OwnerName = owner?.DisplayName,
        });
    }

    private static async Task<IResult> UpdateFamily(
        UpdateFamilyRequest request, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (!await family.CanAsync(Permission.ManageFamily, ct))
            return Forbidden();
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest("Název rodiny je povinný.");

        var fam = await db.Families.FirstOrDefaultAsync(f => f.Id == me.FamilyId, ct);
        if (fam is null)
            return Results.NotFound();

        fam.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.BaseCurrency))
            fam.BaseCurrency = request.BaseCurrency.Trim().ToUpperInvariant();
        fam.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("family.update", "Family", fam.Id.ToString(), fam.Name, fam.Id, "Upravena rodina", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> TransferOwnership(
        TransferOwnershipRequest request, IAppDbContext db, ICurrentFamily family,
        IHubContext<NotificationsHub> hub, IAuditLog audit, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        // Ownership transfer is reserved for the current owner regardless of granted permissions.
        if (me.Role != MemberRole.Owner)
            return Forbidden();

        var target = await db.FamilyMembers
            .FirstOrDefaultAsync(m => m.Id == request.NewOwnerMemberId && m.FamilyId == me.FamilyId, ct);
        if (target is null)
            return Results.BadRequest("Neplatný člen.");
        if (target.Id == me.Id)
            return Results.BadRequest("Jste už vlastníkem rodiny.");
        if (!target.IsActive)
            return Results.BadRequest("Neaktivního člena nelze povýšit na vlastníka.");
        if (target.Role == MemberRole.Child)
            return Results.BadRequest("Dítě nemůže být vlastníkem.");
        if (target.UserId is null)
            return Results.BadRequest("Vlastníkem může být jen člen s vlastním přihlášením.");

        me.Role = MemberRole.Adult;
        target.Role = MemberRole.Owner;
        target.GuardianMemberId = null;
        me.UpdatedAt = target.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await hub.NotifyFamilyAsync(me.FamilyId, ct);
        await audit.RecordAsync("family.transfer-ownership", "Family", me.FamilyId.ToString(), target.DisplayName, me.FamilyId,
            $"Vlastnictví převedeno na {target.DisplayName}", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteFamily(
        DeleteFamilyRequest request, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (me.Role != MemberRole.Owner)
            return Forbidden();

        var fam = await db.Families.FirstOrDefaultAsync(f => f.Id == me.FamilyId, ct);
        if (fam is null)
            return Results.NotFound();
        if (!string.Equals(request.ConfirmName?.Trim(), fam.Name, StringComparison.Ordinal))
            return Results.BadRequest("Pro potvrzení zadejte přesný název rodiny.");

        // All family-scoped data (members, accounts and their transactions/cards, categories,
        // recurring payments, subscriptions, rules, invitations, role permissions) is removed by
        // the database cascade when the family row is deleted.
        var familyId = fam.Id;
        var familyName = fam.Name;
        db.Families.Remove(fam);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("family.delete", "Family", familyId.ToString(), familyName, familyId, "Rodina smazána", ct);
        return Results.NoContent();
    }
}
