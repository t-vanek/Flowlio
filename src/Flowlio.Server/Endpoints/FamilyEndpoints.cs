using Flowlio.Application.Abstractions;
using Flowlio.Application.Mapping;
using Flowlio.Domain;
using Flowlio.Server.Auth;
using Flowlio.Shared;
using Microsoft.EntityFrameworkCore;

namespace Flowlio.Server.Endpoints;

/// <summary>
/// Endpoints for managing family members, invitations (separate logins), per-account access
/// (disponents / spouses), child accounts and payment cards.
/// </summary>
public static class FamilyEndpoints
{
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(14);

    public static void MapFamilyEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/members", GetMembers);
        api.MapPost("/members", CreateMember);
        api.MapDelete("/members/{memberId:guid}", DeleteMember);
        api.MapPost("/members/{memberId:guid}/invite", ReinviteMember);

        api.MapGet("/invitations", GetInvitations);
        api.MapPost("/invitations/{invitationId:guid}/revoke", RevokeInvitation);

        api.MapGet("/accounts/{accountId:guid}/access", GetAccountAccess);
        api.MapPost("/accounts/{accountId:guid}/access", GrantAccountAccess);
        api.MapDelete("/accounts/{accountId:guid}/access/{memberId:guid}", RevokeAccountAccess);

        api.MapGet("/accounts/{accountId:guid}/cards", GetCards);
        api.MapPost("/accounts/{accountId:guid}/cards", CreateCard);
        api.MapPut("/cards/{cardId:guid}", UpdateCard);
        api.MapDelete("/cards/{cardId:guid}", DeleteCard);
    }

    // ---- Members -----------------------------------------------------------

    private static async Task<IReadOnlyList<FamilyMemberDto>> GetMembers(
        IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        var members = await db.FamilyMembers.Where(m => m.FamilyId == me.FamilyId).ToListAsync(ct);

        var pending = (await db.FamilyInvitations
            .Where(i => i.FamilyId == me.FamilyId && i.Status == InvitationStatus.Pending)
            .Select(i => i.MemberId)
            .ToListAsync(ct)).ToHashSet();

        var names = members.ToDictionary(m => m.Id, m => m.DisplayName);

        return members
            .OrderBy(m => m.Role)
            .ThenBy(m => m.DisplayName)
            .Select(m => ToMemberDto(m, pending, names, me.Id))
            .ToList();
    }

    private static async Task<IResult> CreateMember(
        CreateMemberRequest request, IAppDbContext db, ICurrentFamily family, HttpRequest http, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (me.Role != MemberRole.Owner)
            return Forbidden();

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return Results.BadRequest("Jméno je povinné.");
        if (request.Role == MemberRole.Owner)
            return Results.BadRequest("Vlastníka nelze přidat tímto způsobem.");

        Guid? guardianId = null;
        if (request.Role == MemberRole.Child)
        {
            guardianId = request.GuardianMemberId ?? me.Id;
            var guardian = await db.FamilyMembers
                .FirstOrDefaultAsync(m => m.Id == guardianId && m.FamilyId == me.FamilyId, ct);
            if (guardian is null || guardian.Role == MemberRole.Child)
                return Results.BadRequest("Neplatný opatrovník dětského účtu.");
        }

        var member = new FamilyMember
        {
            FamilyId = me.FamilyId,
            DisplayName = request.DisplayName.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Role = request.Role,
            GuardianMemberId = guardianId,
        };
        db.FamilyMembers.Add(member);

        InvitationDto? inviteDto = null;
        if (member.Email is not null)
        {
            var (invitation, url) = NewInvitation(member, http);
            db.FamilyInvitations.Add(invitation);
            inviteDto = ToInvitationDto(invitation, member.DisplayName, member.Role, url);
        }

        await db.SaveChangesAsync(ct);

        var status = inviteDto is not null ? MemberStatus.Pending : MemberStatus.Managed;
        return Results.Ok(new CreateMemberResultDto
        {
            Member = new FamilyMemberDto
            {
                Id = member.Id,
                DisplayName = member.DisplayName,
                Email = member.Email,
                Role = member.Role,
                Status = status,
                GuardianMemberId = member.GuardianMemberId,
            },
            Invitation = inviteDto,
        });
    }

    private static async Task<IResult> DeleteMember(
        Guid memberId, IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (me.Role != MemberRole.Owner)
            return Forbidden();
        if (memberId == me.Id)
            return Results.BadRequest("Sebe sama nelze odebrat.");

        var member = await db.FamilyMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.FamilyId == me.FamilyId, ct);
        if (member is null)
            return Results.NotFound();
        if (member.Role == MemberRole.Owner)
            return Results.BadRequest("Vlastníka rodiny nelze odebrat.");

        var hasDependents = await db.FamilyMembers.AnyAsync(m => m.GuardianMemberId == memberId, ct);
        if (hasDependents)
            return Results.BadRequest("Člen je opatrovníkem dětského účtu. Nejprve přiřaďte dítě jinému opatrovníkovi.");

        db.FamilyMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ReinviteMember(
        Guid memberId, IAppDbContext db, ICurrentFamily family, HttpRequest http, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (me.Role != MemberRole.Owner)
            return Forbidden();

        var member = await db.FamilyMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.FamilyId == me.FamilyId, ct);
        if (member is null)
            return Results.NotFound();
        if (member.UserId is not null)
            return Results.BadRequest("Člen už má vlastní přihlášení.");
        if (string.IsNullOrWhiteSpace(member.Email))
            return Results.BadRequest("Pro pozvánku zadejte u člena e-mail.");

        var existing = await db.FamilyInvitations
            .Where(i => i.MemberId == memberId && i.Status == InvitationStatus.Pending)
            .ToListAsync(ct);
        foreach (var old in existing)
            old.Status = InvitationStatus.Revoked;

        var (invitation, url) = NewInvitation(member, http);
        db.FamilyInvitations.Add(invitation);
        await db.SaveChangesAsync(ct);

        return Results.Ok(ToInvitationDto(invitation, member.DisplayName, member.Role, url));
    }

    // ---- Invitations -------------------------------------------------------

    private static async Task<IReadOnlyList<InvitationDto>> GetInvitations(
        IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        return await db.FamilyInvitations
            .Where(i => i.FamilyId == me.FamilyId && i.Status == InvitationStatus.Pending)
            .OrderBy(i => i.CreatedAt)
            .Select(i => new InvitationDto
            {
                Id = i.Id,
                MemberId = i.MemberId,
                MemberName = i.Member!.DisplayName,
                Email = i.Email,
                Role = i.Member.Role,
                Status = i.Status,
                ExpiresAt = i.ExpiresAt,
            })
            .ToListAsync(ct);
    }

    private static async Task<IResult> RevokeInvitation(
        Guid invitationId, IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (me.Role != MemberRole.Owner)
            return Forbidden();

        var invitation = await db.FamilyInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.FamilyId == me.FamilyId, ct);
        if (invitation is null)
            return Results.NotFound();

        invitation.Status = InvitationStatus.Revoked;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ---- Account access (disponents) --------------------------------------

    private static async Task<IResult> GetAccountAccess(
        Guid accountId, IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        var account = await db.BankAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.FamilyId == me.FamilyId, ct);
        if (account is null)
            return Results.NotFound();

        FamilyMember? owner = account.OwnerMemberId is { } oid
            ? await db.FamilyMembers.FirstOrDefaultAsync(m => m.Id == oid, ct)
            : null;

        var grants = await db.AccountAccesses
            .Where(g => g.BankAccountId == accountId)
            .Select(g => new AccountAccessDto
            {
                Id = g.Id,
                BankAccountId = g.BankAccountId,
                FamilyMemberId = g.FamilyMemberId,
                MemberName = g.Member!.DisplayName,
                Level = g.Level,
            })
            .ToListAsync(ct);

        return Results.Ok(new AccountAccessOverviewDto
        {
            AccountId = accountId,
            OwnerMemberId = account.OwnerMemberId,
            OwnerName = owner?.DisplayName,
            IsChildAccount = owner?.Role == MemberRole.Child,
            Grants = grants,
        });
    }

    private static async Task<IResult> GrantAccountAccess(
        Guid accountId, GrantAccessRequest request, IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        var account = await db.BankAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.FamilyId == me.FamilyId, ct);
        if (account is null)
            return Results.NotFound();
        if (!await CanManageAccountAsync(me, account, db, ct))
            return Forbidden();

        if (request.MemberId == account.OwnerMemberId)
            return Results.BadRequest("Vlastník účtu už k němu má přístup.");

        var member = await db.FamilyMembers
            .FirstOrDefaultAsync(m => m.Id == request.MemberId && m.FamilyId == me.FamilyId, ct);
        if (member is null)
            return Results.BadRequest("Neplatný člen.");

        var grant = await db.AccountAccesses
            .FirstOrDefaultAsync(g => g.BankAccountId == accountId && g.FamilyMemberId == request.MemberId, ct);
        if (grant is null)
        {
            grant = new AccountAccess { BankAccountId = accountId, FamilyMemberId = request.MemberId, Level = request.Level };
            db.AccountAccesses.Add(grant);
        }
        else
        {
            grant.Level = request.Level;
            grant.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        return Results.Ok(new AccountAccessDto
        {
            Id = grant.Id,
            BankAccountId = accountId,
            FamilyMemberId = member.Id,
            MemberName = member.DisplayName,
            Level = grant.Level,
        });
    }

    private static async Task<IResult> RevokeAccountAccess(
        Guid accountId, Guid memberId, IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        var account = await db.BankAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.FamilyId == me.FamilyId, ct);
        if (account is null)
            return Results.NotFound();
        if (!await CanManageAccountAsync(me, account, db, ct))
            return Forbidden();

        var grant = await db.AccountAccesses
            .FirstOrDefaultAsync(g => g.BankAccountId == accountId && g.FamilyMemberId == memberId, ct);
        if (grant is null)
            return Results.NotFound();

        db.AccountAccesses.Remove(grant);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ---- Cards -------------------------------------------------------------

    private static async Task<IResult> GetCards(
        Guid accountId, IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        var account = await db.BankAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.FamilyId == me.FamilyId, ct);
        if (account is null)
            return Results.NotFound();

        var cards = await db.BankCards
            .Where(c => c.BankAccountId == accountId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new BankCardDto
            {
                Id = c.Id,
                BankAccountId = c.BankAccountId,
                HolderMemberId = c.HolderMemberId,
                HolderName = c.Holder != null ? c.Holder.DisplayName : null,
                CardholderName = c.CardholderName,
                Last4 = c.Last4,
                Type = c.Type,
                ExpiryMonth = c.ExpiryMonth,
                ExpiryYear = c.ExpiryYear,
                Status = c.Status,
                MonthlyLimit = c.MonthlyLimit,
            })
            .ToListAsync(ct);

        return Results.Ok(cards);
    }

    private static async Task<IResult> CreateCard(
        Guid accountId, CreateCardRequest request, IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        var account = await db.BankAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.FamilyId == me.FamilyId, ct);
        if (account is null)
            return Results.NotFound();
        if (!await CanManageAccountAsync(me, account, db, ct))
            return Forbidden();

        if (string.IsNullOrWhiteSpace(request.CardholderName))
            return Results.BadRequest("Jméno držitele je povinné.");
        if (request.ExpiryMonth is < 1 or > 12)
            return Results.BadRequest("Neplatný měsíc platnosti.");

        var holderName = await ResolveHolderNameAsync(request.HolderMemberId, me.FamilyId, db, ct);
        if (request.HolderMemberId is not null && holderName is null)
            return Results.BadRequest("Neplatný držitel karty.");

        var card = new BankCard
        {
            BankAccountId = accountId,
            HolderMemberId = request.HolderMemberId,
            CardholderName = request.CardholderName.Trim(),
            Last4 = NormalizeLast4(request.Last4),
            Type = request.Type,
            ExpiryMonth = request.ExpiryMonth,
            ExpiryYear = request.ExpiryYear,
            MonthlyLimit = request.MonthlyLimit,
        };
        db.BankCards.Add(card);
        await db.SaveChangesAsync(ct);

        return Results.Ok(mapper.ToDto(card) with { HolderName = holderName });
    }

    private static async Task<IResult> UpdateCard(
        Guid cardId, UpdateCardRequest request, IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        var card = await db.BankCards
            .Include(c => c.BankAccount)
            .FirstOrDefaultAsync(c => c.Id == cardId && c.BankAccount!.FamilyId == me.FamilyId, ct);
        if (card is null)
            return Results.NotFound();
        if (!await CanManageAccountAsync(me, card.BankAccount!, db, ct))
            return Forbidden();

        if (string.IsNullOrWhiteSpace(request.CardholderName))
            return Results.BadRequest("Jméno držitele je povinné.");
        if (request.ExpiryMonth is < 1 or > 12)
            return Results.BadRequest("Neplatný měsíc platnosti.");

        var holderName = await ResolveHolderNameAsync(request.HolderMemberId, me.FamilyId, db, ct);
        if (request.HolderMemberId is not null && holderName is null)
            return Results.BadRequest("Neplatný držitel karty.");

        card.HolderMemberId = request.HolderMemberId;
        card.CardholderName = request.CardholderName.Trim();
        card.Last4 = NormalizeLast4(request.Last4);
        card.Type = request.Type;
        card.ExpiryMonth = request.ExpiryMonth;
        card.ExpiryYear = request.ExpiryYear;
        card.Status = request.Status;
        card.MonthlyLimit = request.MonthlyLimit;
        card.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(mapper.ToDto(card) with { HolderName = holderName });
    }

    private static async Task<IResult> DeleteCard(
        Guid cardId, IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        var card = await db.BankCards
            .Include(c => c.BankAccount)
            .FirstOrDefaultAsync(c => c.Id == cardId && c.BankAccount!.FamilyId == me.FamilyId, ct);
        if (card is null)
            return Results.NotFound();
        if (!await CanManageAccountAsync(me, card.BankAccount!, db, ct))
            return Forbidden();

        db.BankCards.Remove(card);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ---- Helpers -----------------------------------------------------------

    private static FamilyMemberDto ToMemberDto(
        FamilyMember m, HashSet<Guid> pendingMemberIds, Dictionary<Guid, string> names, Guid currentMemberId)
    {
        var status = m.UserId is not null
            ? MemberStatus.Active
            : pendingMemberIds.Contains(m.Id) ? MemberStatus.Pending : MemberStatus.Managed;

        return new FamilyMemberDto
        {
            Id = m.Id,
            DisplayName = m.DisplayName,
            Email = m.Email,
            Role = m.Role,
            Status = status,
            GuardianMemberId = m.GuardianMemberId,
            GuardianName = m.GuardianMemberId is { } g && names.TryGetValue(g, out var gn) ? gn : null,
            IsCurrentUser = m.Id == currentMemberId,
        };
    }

    private static (FamilyInvitation Invitation, string Url) NewInvitation(FamilyMember member, HttpRequest http)
    {
        var (raw, hash) = InvitationTokens.Generate();
        var invitation = new FamilyInvitation
        {
            FamilyId = member.FamilyId,
            MemberId = member.Id,
            Email = member.Email!,
            TokenHash = hash,
            Status = InvitationStatus.Pending,
            ExpiresAt = DateTimeOffset.UtcNow.Add(InviteLifetime),
        };
        var url = $"{http.Scheme}://{http.Host}/Account/Register?invite={Uri.EscapeDataString(raw)}";
        return (invitation, url);
    }

    private static InvitationDto ToInvitationDto(FamilyInvitation inv, string memberName, MemberRole role, string? url) =>
        new()
        {
            Id = inv.Id,
            MemberId = inv.MemberId,
            MemberName = memberName,
            Email = inv.Email,
            Role = role,
            Status = inv.Status,
            ExpiresAt = inv.ExpiresAt,
            InviteUrl = url,
        };

    private static async Task<string?> ResolveHolderNameAsync(
        Guid? holderMemberId, Guid familyId, IAppDbContext db, CancellationToken ct)
    {
        if (holderMemberId is not { } id)
            return null;
        return await db.FamilyMembers
            .Where(m => m.Id == id && m.FamilyId == familyId)
            .Select(m => m.DisplayName)
            .FirstOrDefaultAsync(ct);
    }

    private static string? NormalizeLast4(string? last4)
    {
        if (string.IsNullOrWhiteSpace(last4))
            return null;
        var digits = new string(last4.Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? digits : digits[^4..];
    }

    /// <summary>The family owner, the account's owner member, or a child owner's guardian may manage an account.</summary>
    private static async Task<bool> CanManageAccountAsync(
        FamilyMember me, BankAccount account, IAppDbContext db, CancellationToken ct)
    {
        if (me.Role == MemberRole.Owner)
            return true;
        if (account.OwnerMemberId == me.Id)
            return true;
        if (account.OwnerMemberId is { } oid)
        {
            var owner = await db.FamilyMembers.FirstOrDefaultAsync(m => m.Id == oid, ct);
            if (owner is { Role: MemberRole.Child } && owner.GuardianMemberId == me.Id)
                return true;
        }
        return false;
    }

    private static IResult Forbidden() =>
        Results.Problem(detail: "Na tuto akci nemáte oprávnění.", statusCode: StatusCodes.Status403Forbidden);
}
