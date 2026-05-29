using Flowlio.Application.Abstractions;
using Flowlio.Application.Mapping;
using Flowlio.Application.Statements;
using Flowlio.Domain;
using Flowlio.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using static Flowlio.Server.Auth.MemberAuthorization;

namespace Flowlio.Server.Endpoints;

/// <summary>
/// Endpoints for managing categorization rules and re-running them over existing transactions.
/// Creating or updating a rule immediately backfills uncategorized transactions so the rule takes
/// effect on history, not just on the next import.
/// </summary>
public static class RuleEndpoints
{
    public static void MapRuleEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/rules", GetRules);
        api.MapGet("/rules/deleted", GetDeletedRules);
        api.MapGet("/rules/suggestions", GetSuggestions);
        api.MapPost("/rules/suggestions/dismiss", DismissSuggestion);
        api.MapPost("/rules", CreateRule);
        api.MapPut("/rules/{id:guid}", UpdateRule);
        api.MapDelete("/rules/{id:guid}", DeleteRule);
        api.MapPost("/rules/{id:guid}/restore", RestoreRule);
        api.MapPost("/rules/recategorize", Recategorize);
    }

    /// <summary>Number of times the same counterparty must be manually categorized into the same category
    /// before Flowlio suggests turning it into a rule.</summary>
    private const int SuggestionThreshold = 2;

    /// <summary>Suggests personal rules learned from the member's own repeated manual categorization:
    /// counterparties they filed into the same category at least <see cref="SuggestionThreshold"/> times on
    /// their own accounts, and which the rules applicable to those accounts don't already categorize that way.
    /// Accepting creates a personal rule. The suggestions only cover the member's owned accounts, matching the
    /// scope a learned personal rule would have.</summary>
    private static async Task<IResult> GetSuggestions(
        IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var ownedAccountIds = await db.BankAccounts
            .Where(a => a.FamilyId == me.FamilyId && a.OwnerMemberId == me.Id)
            .Select(a => a.Id)
            .ToListAsync(ct);
        if (ownedAccountIds.Count == 0)
            return Results.Ok(new List<RuleSuggestionDto>());

        var manual = await db.Transactions
            .Where(t => t.FamilyId == me.FamilyId
                && t.CategorySource == CategorySource.Manual
                && t.CategoryId != null
                && t.CounterpartyName != null
                && t.CounterpartyName != ""
                && ownedAccountIds.Contains(t.BankAccountId))
            .Select(t => new { Name = t.CounterpartyName!, t.Direction, t.BankAccountId, CategoryId = t.CategoryId!.Value })
            .ToListAsync(ct);
        if (manual.Count == 0)
            return Results.Ok(new List<RuleSuggestionDto>());

        var dismissed = (await db.RuleSuggestionDismissals
                .Where(d => d.FamilyId == me.FamilyId)
                .Select(d => new { d.CounterpartyKey, d.CategoryId })
                .ToListAsync(ct))
            .Select(d => (d.CounterpartyKey, d.CategoryId))
            .ToHashSet();

        var familyRules = await db.CategorizationRules
            .Include(r => r.Category)
            .Where(r => r.FamilyId == me.FamilyId && r.IsActive)
            .ToListAsync(ct);

        var categoryNames = await db.Categories
            .Where(c => c.FamilyId == me.FamilyId)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var suggestions = manual
            // Consolidate case variants of the same merchant filed into the same category.
            .GroupBy(x => (Key: NormalizeKey(x.Name), x.CategoryId))
            .Where(g => g.Key.Key.Length > 0 && g.Count() >= SuggestionThreshold)
            .Where(g => !dismissed.Contains((g.Key.Key, g.Key.CategoryId)))
            .Select(g =>
            {
                // Show the merchant's most common original spelling as the rule pattern.
                var pattern = g.GroupBy(x => x.Name.Trim())
                    .OrderByDescending(s => s.Count())
                    .First().Key;
                var sample = g.First();
                return (Pattern: pattern, g.Key.CategoryId, Count: g.Count(), sample.Direction, sample.BankAccountId);
            })
            // Skip merchants the rules applicable to that account already file into this same category.
            .Where(s => TransactionCategorizer.Match(
                s.Pattern, null, null, null, s.Direction,
                TransactionCategorizer.ForAccount(familyRules, s.BankAccountId, me.Id)) != s.CategoryId)
            .OrderByDescending(s => s.Count)
            .ThenBy(s => s.Pattern)
            .Take(20)
            .Select(s => new RuleSuggestionDto
            {
                Pattern = s.Pattern,
                CategoryId = s.CategoryId,
                CategoryName = categoryNames.GetValueOrDefault(s.CategoryId, "—"),
                MatchCount = s.Count,
            })
            .ToList();

        return Results.Ok(suggestions);
    }

    /// <summary>Permanently hides a suggestion for a counterparty + category (idempotent).</summary>
    private static async Task<IResult> DismissSuggestion(
        RuleSuggestionDismissRequest request, IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var key = NormalizeKey(request.Pattern);
        if (key.Length == 0)
            return Results.BadRequest("Prázdný vzor.");

        var alreadyDismissed = await db.RuleSuggestionDismissals.AnyAsync(
            d => d.FamilyId == familyId && d.CounterpartyKey == key && d.CategoryId == request.CategoryId, ct);
        if (!alreadyDismissed)
        {
            db.RuleSuggestionDismissals.Add(new RuleSuggestionDismissal
            {
                FamilyId = familyId,
                CounterpartyKey = key,
                CategoryId = request.CategoryId,
            });
            await db.SaveChangesAsync(ct);
        }
        return Results.NoContent();
    }

    /// <summary>Normalized counterparty key used for both grouping suggestions and matching dismissals.</summary>
    private static string NormalizeKey(string name) => name.Trim().ToLowerInvariant();

    private static async Task<IResult> GetRules(
        IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();
        var isOwner = me.Role == MemberRole.Owner;

        var rules = await VisibleRules(db, me, isOwner)
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync(ct);

        return Results.Ok(rules.Select(r => ToScopedDto(db, mapper, r, me, isOwner)).ToList());
    }

    /// <summary>Soft-deleted rules, newest first, for the "deleted rules" panel and restore.</summary>
    private static async Task<IResult> GetDeletedRules(
        IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();
        var isOwner = me.Role == MemberRole.Owner;

        var rules = await VisibleRules(db, me, isOwner)
            .IgnoreQueryFilters()
            .Where(r => r.FamilyId == me.FamilyId && r.DeletedAt != null)
            .OrderByDescending(r => r.DeletedAt)
            .ToListAsync(ct);

        return Results.Ok(rules.Select(r => ToScopedDto(db, mapper, r, me, isOwner)).ToList());
    }

    /// <summary>Rules the member may see: the owner sees the whole family's rules; everyone else sees only
    /// their own personal rules (the only kind they can manage).</summary>
    private static IQueryable<CategorizationRule> VisibleRules(IAppDbContext db, FamilyMember me, bool isOwner)
    {
        var q = db.CategorizationRules
            .Include(r => r.Category)
            .Include(r => r.BankAccount)
            .Include(r => r.OwnerMember)
            .Where(r => r.FamilyId == me.FamilyId);
        return isOwner ? q : q.Where(r => r.Scope == RuleScope.Personal && r.OwnerMemberId == me.Id);
    }

    private static async Task<IResult> CreateRule(
        CategorizationRuleRequest request, IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper,
        IDistributedCache cache, IAuditLog audit, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();
        var isOwner = me.Role == MemberRole.Owner;

        if (await ValidateScope(me, isOwner, request, db, ct) is { } error)
            return error;
        if (!await CategoryBelongsToFamily(db, me.FamilyId, request.CategoryId, ct))
            return Results.BadRequest("Neplatná kategorie.");
        if (InvalidRegex(request))
            return Results.BadRequest("Neplatný regulární výraz.");

        var rule = new CategorizationRule
        {
            FamilyId = me.FamilyId,
            Scope = request.Scope,
            OwnerMemberId = request.Scope == RuleScope.Personal ? me.Id : null,
            BankAccountId = request.Scope == RuleScope.Account ? request.BankAccountId : null,
            Field = request.Field,
            MatchMode = request.MatchMode,
            Pattern = request.Pattern.Trim(),
            CategoryId = request.CategoryId,
            Priority = request.Priority,
            IsActive = request.IsActive,
        };
        db.CategorizationRules.Add(rule);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("rule.create", "CategorizationRule", rule.Id.ToString(), rule.Pattern, me.FamilyId,
            $"Vytvořeno pravidlo „{rule.Pattern}“", ct);

        // Backfill uncategorized transactions so the new rule applies to history too.
        await RecategorizeAsync(db, cache, me.FamilyId, onlyUncategorized: true, ct);

        return Results.Ok(await LoadDtoAsync(db, mapper, rule.Id, me, isOwner, ct));
    }

    private static async Task<IResult> UpdateRule(
        Guid id, CategorizationRuleRequest request, IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper,
        IDistributedCache cache, IAuditLog audit, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();
        var isOwner = me.Role == MemberRole.Owner;

        var rule = await db.CategorizationRules
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == me.FamilyId, ct);
        if (rule is null)
            return Results.NotFound();
        if (!CanManageRule(rule, me, isOwner))
            return Forbidden();
        if (await ValidateScope(me, isOwner, request, db, ct) is { } error)
            return error;
        if (!await CategoryBelongsToFamily(db, me.FamilyId, request.CategoryId, ct))
            return Results.BadRequest("Neplatná kategorie.");
        if (InvalidRegex(request))
            return Results.BadRequest("Neplatný regulární výraz.");

        rule.Scope = request.Scope;
        rule.OwnerMemberId = request.Scope == RuleScope.Personal ? me.Id : null;
        rule.BankAccountId = request.Scope == RuleScope.Account ? request.BankAccountId : null;
        rule.Field = request.Field;
        rule.MatchMode = request.MatchMode;
        rule.Pattern = request.Pattern.Trim();
        rule.CategoryId = request.CategoryId;
        rule.Priority = request.Priority;
        rule.IsActive = request.IsActive;
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        // Optimistic concurrency: a stale Version makes SaveChanges throw, turned into HTTP 409 (Program.cs).
        db.SetOriginalRowVersion(rule, request.Version);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("rule.update", "CategorizationRule", rule.Id.ToString(), rule.Pattern, me.FamilyId,
            $"Upraveno pravidlo „{rule.Pattern}“", ct);

        await RecategorizeAsync(db, cache, me.FamilyId, onlyUncategorized: true, ct);

        return Results.Ok(await LoadDtoAsync(db, mapper, rule.Id, me, isOwner, ct));
    }

    private static async Task<IResult> DeleteRule(
        Guid id, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var rule = await db.CategorizationRules
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == me.FamilyId, ct);
        if (rule is null)
            return Results.NotFound();
        if (!CanManageRule(rule, me, me.Role == MemberRole.Owner))
            return Forbidden();

        // Soft delete: the rule stops categorizing but is recoverable via restore. Already-categorized
        // transactions keep their category (we don't track which rule assigned what).
        rule.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("rule.delete", "CategorizationRule", id.ToString(), rule.Pattern, me.FamilyId,
            $"Smazáno pravidlo „{rule.Pattern}“", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RestoreRule(
        Guid id, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var rule = await db.CategorizationRules
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == me.FamilyId && r.DeletedAt != null, ct);
        if (rule is null)
            return Results.NotFound();
        if (!CanManageRule(rule, me, me.Role == MemberRole.Owner))
            return Forbidden();

        rule.DeletedAt = null;
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("rule.restore", "CategorizationRule", id.ToString(), rule.Pattern, me.FamilyId,
            $"Obnoveno pravidlo „{rule.Pattern}“", ct);
        return Results.NoContent();
    }

    /// <summary>The owner manages every rule; anyone else manages only their own personal rules.</summary>
    private static bool CanManageRule(CategorizationRule rule, FamilyMember me, bool isOwner) =>
        isOwner || (rule.Scope == RuleScope.Personal && rule.OwnerMemberId == me.Id);

    /// <summary>Authorizes the requested scope: family/account rules are owner-only and an account rule must
    /// target a real family account. Returns null when allowed, otherwise the error result (as a task).</summary>
    private static Task<IResult?> ValidateScope(
        FamilyMember me, bool isOwner, CategorizationRuleRequest request, IAppDbContext db, CancellationToken ct) =>
        request.Scope switch
        {
            RuleScope.Personal => Task.FromResult<IResult?>(null),
            RuleScope.Family => Task.FromResult<IResult?>(isOwner ? null : Forbidden()),
            RuleScope.Account => ValidateAccountScope(me, isOwner, request, db, ct),
            _ => Task.FromResult<IResult?>(Results.BadRequest("Neplatný rozsah pravidla.")),
        };

    private static async Task<IResult?> ValidateAccountScope(
        FamilyMember me, bool isOwner, CategorizationRuleRequest request, IAppDbContext db, CancellationToken ct)
    {
        if (!isOwner)
            return Forbidden();
        if (request.BankAccountId is not { } accountId ||
            !await db.BankAccounts.AnyAsync(a => a.Id == accountId && a.FamilyId == me.FamilyId, ct))
            return Results.BadRequest("Neplatný účet pro pravidlo.");
        return null;
    }

    private static CategorizationRuleDto ToScopedDto(
        IAppDbContext db, FlowlioMapper mapper, CategorizationRule r, FamilyMember me, bool isOwner) =>
        mapper.ToDto(r) with
        {
            Version = db.GetRowVersion(r),
            BankAccountName = r.BankAccount?.Name,
            OwnerName = r.OwnerMember?.DisplayName,
            CanManage = CanManageRule(r, me, isOwner),
        };

    private static async Task<IResult> Recategorize(
        RecategorizeRequest request, IAppDbContext db, ICurrentFamily family,
        IDistributedCache cache, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var count = await RecategorizeAsync(db, cache, familyId, request.OnlyUncategorized, ct);
        await audit.RecordAsync("rule.recategorize", "Transaction", null, $"{count} pohybů", familyId,
            $"Přepočet kategorií ({count} pohybů zařazeno)", ct);
        return Results.Ok(new BulkResultDto { Count = count });
    }

    /// <summary>Runs the family's active rules over its transactions, assigning a category where one matches.
    /// Never clears an existing category; with <paramref name="onlyUncategorized"/> it also skips rows that
    /// already have one. Returns the number of transactions changed.</summary>
    private static async Task<int> RecategorizeAsync(
        IAppDbContext db, IDistributedCache cache, Guid familyId, bool onlyUncategorized, CancellationToken ct)
    {
        var familyRules = await db.CategorizationRules
            .Include(r => r.Category)
            .Where(r => r.FamilyId == familyId && r.IsActive)
            .ToListAsync(ct);
        if (familyRules.Count == 0)
            return 0;

        // Never touch human choices: rules only fill in or replace rule-assigned/empty categories.
        var query = db.Transactions.Where(t => t.FamilyId == familyId && t.CategorySource != CategorySource.Manual);
        if (onlyUncategorized)
            query = query.Where(t => t.CategoryId == null);
        var transactions = await query.ToListAsync(ct);
        if (transactions.Count == 0)
            return 0;

        // Resolve account ownership so personal/account-scoped rules apply to the right rows.
        var owners = await db.BankAccounts
            .Where(a => a.FamilyId == familyId)
            .Select(a => new { a.Id, a.OwnerMemberId })
            .ToDictionaryAsync(a => a.Id, a => a.OwnerMemberId, ct);

        // The scope-ordered rule list is the same for every transaction on a given account — resolve once.
        var rulesByAccount = new Dictionary<Guid, IReadOnlyList<CategorizationRule>>();
        IReadOnlyList<CategorizationRule> RulesFor(Guid accountId) =>
            rulesByAccount.TryGetValue(accountId, out var cached)
                ? cached
                : rulesByAccount[accountId] =
                    TransactionCategorizer.ForAccount(familyRules, accountId, owners.GetValueOrDefault(accountId));

        var now = DateTimeOffset.UtcNow;
        var changed = 0;
        foreach (var t in transactions)
        {
            var match = TransactionCategorizer.Match(
                t.CounterpartyName, t.Description, t.VariableSymbol, t.CounterpartyAccount, t.Direction,
                RulesFor(t.BankAccountId));
            if (match is { } categoryId && categoryId != t.CategoryId)
            {
                t.CategoryId = categoryId;
                t.CategorySource = CategorySource.Rule;
                t.UpdatedAt = now;
                changed++;
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(ct);
            await cache.RemoveAsync(CacheKeys.Dashboard(familyId), ct);
        }
        return changed;
    }

    private static async Task<CategorizationRuleDto> LoadDtoAsync(
        IAppDbContext db, FlowlioMapper mapper, Guid ruleId, FamilyMember me, bool isOwner, CancellationToken ct)
    {
        var rule = await db.CategorizationRules
            .Include(r => r.Category)
            .Include(r => r.BankAccount)
            .Include(r => r.OwnerMember)
            .FirstAsync(r => r.Id == ruleId, ct);
        return ToScopedDto(db, mapper, rule, me, isOwner);
    }

    private static async Task<bool> CategoryBelongsToFamily(
        IAppDbContext db, Guid familyId, Guid categoryId, CancellationToken ct) =>
        await db.Categories.AnyAsync(c => c.Id == categoryId && c.FamilyId == familyId, ct);

    /// <summary>True when the rule uses regex mode but the pattern doesn't compile, so we reject it early
    /// instead of silently never matching at import time.</summary>
    private static bool InvalidRegex(CategorizationRuleRequest request) =>
        request.MatchMode == RuleMatchMode.Regex && !TransactionCategorizer.IsValidRegex(request.Pattern.Trim());
}
