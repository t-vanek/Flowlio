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
        api.MapPost("/rules/preview", PreviewRule);
        api.MapPost("/rules", CreateRule);
        api.MapPut("/rules/{id:guid}", UpdateRule);
        api.MapDelete("/rules/{id:guid}", DeleteRule);
        api.MapPost("/rules/{id:guid}/restore", RestoreRule);
        api.MapPost("/rules/recategorize", Recategorize);
        api.MapPost("/rules/reorder", ReorderRules);
        api.MapGet("/rules/export", ExportRules);
        api.MapPost("/rules/import", ImportRules);
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
            .Select(t => new { Name = t.CounterpartyName!, t.Direction, t.BankAccountId, t.Amount, t.Currency, CategoryId = t.CategoryId!.Value })
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
                return (Pattern: pattern, g.Key.CategoryId, Count: g.Count(),
                    sample.Direction, sample.BankAccountId, sample.Amount, sample.Currency);
            })
            // Skip merchants the rules applicable to that account already file into this same category.
            .Where(s => TransactionCategorizer.Match(
                s.Pattern, null, null, null, s.Amount, s.Currency, s.Direction,
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

        var ruleIds = rules.Select(r => r.Id).ToList();
        var usage = await db.Transactions
            .Where(t => t.FamilyId == me.FamilyId && t.AppliedRuleId != null && ruleIds.Contains(t.AppliedRuleId!.Value))
            .GroupBy(t => t.AppliedRuleId!.Value)
            .Select(g => new { RuleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RuleId, x => x.Count, ct);

        return Results.Ok(rules.Select(r => ToScopedDto(db, mapper, r, me, isOwner, usage.GetValueOrDefault(r.Id))).ToList());
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

    /// <summary>Dry-run: how many transactions the (unsaved) rule would match in its scope, how many already
    /// have a different non-manual category it would change, and a few examples. Lets the user see the impact
    /// of a regex / amount / global rule before committing.</summary>
    private static async Task<IResult> PreviewRule(
        CategorizationRuleRequest request, IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();
        var isOwner = me.Role == MemberRole.Owner;

        if (await ValidateScope(me, isOwner, request, db, ct) is { } scopeError)
            return scopeError;
        if (ValidateConditions(request) is { } condError)
            return condError;

        var category = await db.Categories
            .FirstOrDefaultAsync(c => c.Id == request.CategoryId && c.FamilyId == me.FamilyId, ct);
        if (category is null)
            return Results.BadRequest("Neplatná kategorie.");

        // Build the rule as it would be saved (with its category, for the income/expense filter).
        var candidate = new CategorizationRule
        {
            FamilyId = me.FamilyId,
            Scope = request.Scope,
            OwnerMemberId = request.Scope == RuleScope.Personal ? me.Id : null,
            BankAccountId = request.Scope == RuleScope.Account ? request.BankAccountId : null,
            CategoryId = request.CategoryId,
            Category = category,
            IsActive = true,
        };
        ApplyConditions(candidate, request);
        var rules = new[] { candidate };

        // Restrict to the transactions the rule's scope can reach.
        var query = db.Transactions.Where(t => t.FamilyId == me.FamilyId && t.BankAccount!.DeletedAt == null);
        if (request.Scope == RuleScope.Account)
        {
            query = query.Where(t => t.BankAccountId == request.BankAccountId);
        }
        else if (request.Scope == RuleScope.Personal)
        {
            var owned = await db.BankAccounts
                .Where(a => a.FamilyId == me.FamilyId && a.OwnerMemberId == me.Id)
                .Select(a => a.Id)
                .ToListAsync(ct);
            query = query.Where(t => owned.Contains(t.BankAccountId));
        }

        var transactions = await query
            .Select(t => new
            {
                t.CounterpartyName, t.Description, t.VariableSymbol, t.CounterpartyAccount,
                t.Amount, t.Currency, t.Direction, t.BookingDate,
                t.CategoryId, t.CategorySource, CategoryName = t.Category!.Name,
            })
            .ToListAsync(ct);

        var matches = 0;
        var wouldRecategorize = 0;
        var samples = new List<RulePreviewSampleDto>();
        foreach (var t in transactions)
        {
            if (TransactionCategorizer.MatchRule(
                    t.CounterpartyName, t.Description, t.VariableSymbol, t.CounterpartyAccount,
                    t.Amount, t.Currency, t.Direction, rules) is null)
                continue;

            matches++;
            // Manual categories are protected; only a different rule/empty category would actually change.
            if (t.CategorySource != CategorySource.Manual && t.CategoryId != null && t.CategoryId != request.CategoryId)
                wouldRecategorize++;
            if (samples.Count < 5)
                samples.Add(new RulePreviewSampleDto
                {
                    BookingDate = t.BookingDate,
                    Counterparty = t.CounterpartyName,
                    Amount = t.Amount,
                    Currency = t.Currency,
                    CurrentCategoryName = t.CategoryId == null ? null : t.CategoryName,
                });
        }

        return Results.Ok(new RulePreviewDto { Matches = matches, WouldRecategorize = wouldRecategorize, Samples = samples });
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
        if (ValidateConditions(request) is { } condError)
            return condError;

        var rule = new CategorizationRule
        {
            FamilyId = me.FamilyId,
            Scope = request.Scope,
            OwnerMemberId = request.Scope == RuleScope.Personal ? me.Id : null,
            BankAccountId = request.Scope == RuleScope.Account ? request.BankAccountId : null,
            CategoryId = request.CategoryId,
            Priority = request.Priority,
            IsActive = request.IsActive,
        };
        ApplyConditions(rule, request);
        db.CategorizationRules.Add(rule);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("rule.create", "CategorizationRule", rule.Id.ToString(), DescribeRule(rule), me.FamilyId,
            $"Vytvořeno pravidlo „{DescribeRule(rule)}“", ct);

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
        if (ValidateConditions(request) is { } condError)
            return condError;

        rule.Scope = request.Scope;
        rule.OwnerMemberId = request.Scope == RuleScope.Personal ? me.Id : null;
        rule.BankAccountId = request.Scope == RuleScope.Account ? request.BankAccountId : null;
        rule.CategoryId = request.CategoryId;
        rule.Priority = request.Priority;
        rule.IsActive = request.IsActive;
        ApplyConditions(rule, request);
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        // Optimistic concurrency: a stale Version makes SaveChanges throw, turned into HTTP 409 (Program.cs).
        db.SetOriginalRowVersion(rule, request.Version);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("rule.update", "CategorizationRule", rule.Id.ToString(), DescribeRule(rule), me.FamilyId,
            $"Upraveno pravidlo „{DescribeRule(rule)}“", ct);

        // Re-evaluate uncategorized rows and the rows this rule had assigned, so the edit takes effect on history.
        await RecategorizeAsync(db, cache, me.FamilyId, onlyUncategorized: true, ct, reevaluateRuleId: rule.Id);

        return Results.Ok(await LoadDtoAsync(db, mapper, rule.Id, me, isOwner, ct));
    }

    private static async Task<IResult> DeleteRule(
        Guid id, IAppDbContext db, ICurrentFamily family, IDistributedCache cache, IAuditLog audit, CancellationToken ct)
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

        // Soft delete: recoverable via restore. The rule stops categorizing immediately.
        rule.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        // Re-evaluate the rows this rule had assigned: another rule may now claim them, otherwise they
        // return to uncategorized. Manual choices are untouched.
        await RecategorizeAsync(db, cache, me.FamilyId, onlyUncategorized: false, ct, reevaluateRuleId: rule.Id);
        await audit.RecordAsync("rule.delete", "CategorizationRule", id.ToString(), DescribeRule(rule), me.FamilyId,
            $"Smazáno pravidlo „{DescribeRule(rule)}“", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RestoreRule(
        Guid id, IAppDbContext db, ICurrentFamily family, IDistributedCache cache, IAuditLog audit, CancellationToken ct)
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
        // The restored rule applies to uncategorized history again.
        await RecategorizeAsync(db, cache, me.FamilyId, onlyUncategorized: true, ct);
        await audit.RecordAsync("rule.restore", "CategorizationRule", id.ToString(), DescribeRule(rule), me.FamilyId,
            $"Obnoveno pravidlo „{DescribeRule(rule)}“", ct);
        return Results.NoContent();
    }

    /// <summary>Persists a new priority order (highest first) for the manageable rules in the list.</summary>
    private static async Task<IResult> ReorderRules(
        ReorderRulesRequest request, IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();
        var isOwner = me.Role == MemberRole.Owner;

        var ids = request.OrderedIds;
        var rules = await db.CategorizationRules
            .Where(r => r.FamilyId == me.FamilyId && ids.Contains(r.Id))
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < ids.Count; i++)
        {
            var rule = rules.FirstOrDefault(r => r.Id == ids[i]);
            if (rule is null || !CanManageRule(rule, me, isOwner))
                continue;
            rule.Priority = ids.Count - i; // first in the list gets the highest priority
            rule.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Exports the member's visible rules as portable definitions (category/account by name).</summary>
    private static async Task<IResult> ExportRules(
        IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();
        var isOwner = me.Role == MemberRole.Owner;

        var rules = await VisibleRules(db, me, isOwner)
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync(ct);

        var export = rules.Select(r => new RuleExportDto
        {
            Scope = r.Scope,
            BankAccountName = r.BankAccount?.Name,
            Field = r.Field,
            MatchMode = r.MatchMode,
            Pattern = r.Pattern,
            MinAmount = r.MinAmount,
            MaxAmount = r.MaxAmount,
            AmountCurrency = r.AmountCurrency,
            CategoryName = r.Category?.Name ?? "",
            Priority = r.Priority,
            IsActive = r.IsActive,
        }).ToList();

        return Results.Ok(export);
    }

    /// <summary>Imports rule definitions, resolving category/account by name. Skips rows whose category/account
    /// is missing, whose scope the member may not create, or that are otherwise invalid.</summary>
    private static async Task<IResult> ImportRules(
        IReadOnlyList<RuleExportDto> items, IAppDbContext db, ICurrentFamily family,
        IDistributedCache cache, IAuditLog audit, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();
        var isOwner = me.Role == MemberRole.Owner;

        var categories = await db.Categories.Where(c => c.FamilyId == me.FamilyId).ToListAsync(ct);
        var accounts = await db.BankAccounts.Where(a => a.FamilyId == me.FamilyId).ToListAsync(ct);

        var imported = 0;
        var skipped = 0;
        foreach (var item in items ?? [])
        {
            var category = categories.FirstOrDefault(c => c.Name == item.CategoryName);
            var hasText = !string.IsNullOrWhiteSpace(item.Pattern);
            var hasAmount = item.MinAmount is not null || item.MaxAmount is not null;

            var allowedScope = item.Scope == RuleScope.Personal || isOwner;
            var validAmount = !hasAmount || (!string.IsNullOrWhiteSpace(item.AmountCurrency) && item.AmountCurrency.Trim().Length == 3);
            var validRegex = !hasText || item.MatchMode != RuleMatchMode.Regex || TransactionCategorizer.IsValidRegex(item.Pattern!.Trim());

            Guid? bankAccountId = null;
            if (item.Scope == RuleScope.Account)
                bankAccountId = accounts.FirstOrDefault(a => a.Name == item.BankAccountName)?.Id;

            if (category is null || !allowedScope || (!hasText && !hasAmount) || !validAmount || !validRegex
                || (item.Scope == RuleScope.Account && bankAccountId is null))
            {
                skipped++;
                continue;
            }

            db.CategorizationRules.Add(new CategorizationRule
            {
                FamilyId = me.FamilyId,
                Scope = item.Scope,
                OwnerMemberId = item.Scope == RuleScope.Personal ? me.Id : null,
                BankAccountId = bankAccountId,
                Field = item.Field,
                MatchMode = item.MatchMode,
                Pattern = hasText ? item.Pattern!.Trim() : null,
                MinAmount = hasAmount ? item.MinAmount : null,
                MaxAmount = hasAmount ? item.MaxAmount : null,
                AmountCurrency = hasAmount ? item.AmountCurrency!.Trim().ToUpperInvariant() : null,
                CategoryId = category.Id,
                Priority = item.Priority,
                IsActive = item.IsActive,
            });
            imported++;
        }

        if (imported > 0)
        {
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync("rule.import", "CategorizationRule", null, $"{imported} pravidel", me.FamilyId,
                $"Import pravidel ({imported} importováno, {skipped} přeskočeno)", ct);
            await RecategorizeAsync(db, cache, me.FamilyId, onlyUncategorized: true, ct);
        }

        return Results.Ok(new RuleImportResultDto { Imported = imported, Skipped = skipped });
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
        IAppDbContext db, FlowlioMapper mapper, CategorizationRule r, FamilyMember me, bool isOwner, int usageCount = 0) =>
        mapper.ToDto(r) with
        {
            Version = db.GetRowVersion(r),
            BankAccountName = r.BankAccount?.Name,
            OwnerName = r.OwnerMember?.DisplayName,
            CanManage = CanManageRule(r, me, isOwner),
            UsageCount = usageCount,
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

    /// <summary>Runs the family's active rules over its transactions, assigning a category (and recording the
    /// rule that did it) where one matches. Manual choices are never touched. With <paramref name="onlyUncategorized"/>
    /// it only fills empty rows. When <paramref name="reevaluateRuleId"/> is set (after a rule edit/delete) it also
    /// re-checks rows that rule had assigned, and clears any that no longer match any rule. Returns rows changed.</summary>
    private static async Task<int> RecategorizeAsync(
        IAppDbContext db, IDistributedCache cache, Guid familyId, bool onlyUncategorized, CancellationToken ct,
        Guid? reevaluateRuleId = null)
    {
        var familyRules = await db.CategorizationRules
            .Include(r => r.Category)
            .Where(r => r.FamilyId == familyId && r.IsActive)
            .ToListAsync(ct);

        // Never touch human choices: rules only fill in or replace rule-assigned/empty categories.
        var query = db.Transactions.Where(t => t.FamilyId == familyId && t.CategorySource != CategorySource.Manual);
        if (reevaluateRuleId is { } rid)
            query = query.Where(t => t.CategoryId == null || t.AppliedRuleId == rid);
        else if (onlyUncategorized)
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
            var matched = TransactionCategorizer.MatchRule(
                t.CounterpartyName, t.Description, t.VariableSymbol, t.CounterpartyAccount,
                t.Amount, t.Currency, t.Direction, RulesFor(t.BankAccountId));

            if (matched is { } rule)
            {
                if (t.CategoryId != rule.CategoryId || t.AppliedRuleId != rule.Id || t.CategorySource != CategorySource.Rule)
                {
                    t.CategoryId = rule.CategoryId;
                    t.CategorySource = CategorySource.Rule;
                    t.AppliedRuleId = rule.Id;
                    t.UpdatedAt = now;
                    changed++;
                }
            }
            else if (reevaluateRuleId is not null && t.CategorySource == CategorySource.Rule)
            {
                // Re-evaluating after an edit/delete and nothing matches a previously rule-assigned row: clear it.
                t.CategoryId = null;
                t.CategorySource = CategorySource.None;
                t.AppliedRuleId = null;
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
        var usage = await db.Transactions.CountAsync(t => t.AppliedRuleId == ruleId, ct);
        return ToScopedDto(db, mapper, rule, me, isOwner, usage);
    }

    private static async Task<bool> CategoryBelongsToFamily(
        IAppDbContext db, Guid familyId, Guid categoryId, CancellationToken ct) =>
        await db.Categories.AnyAsync(c => c.Id == categoryId && c.FamilyId == familyId, ct);

    /// <summary>Validates a rule's conditions: it must have a text pattern and/or an amount range; an amount
    /// range needs a 3-letter currency, non-negative bounds and min ≤ max; a regex pattern must compile.</summary>
    private static IResult? ValidateConditions(CategorizationRuleRequest request)
    {
        var hasText = !string.IsNullOrWhiteSpace(request.Pattern);
        var hasAmount = request.MinAmount is not null || request.MaxAmount is not null;

        if (!hasText && !hasAmount)
            return Results.BadRequest("Pravidlo musí mít vzor nebo podmínku na částku.");
        if (hasAmount)
        {
            if (string.IsNullOrWhiteSpace(request.AmountCurrency) || request.AmountCurrency.Trim().Length != 3)
                return Results.BadRequest("U podmínky na částku zadejte měnu (3 znaky).");
            if (request.MinAmount is < 0 || request.MaxAmount is < 0)
                return Results.BadRequest("Částka nesmí být záporná.");
            if (request.MinAmount is { } min && request.MaxAmount is { } max && min > max)
                return Results.BadRequest("Částka „od“ nesmí být větší než „do“.");
        }
        if (hasText && request.MatchMode == RuleMatchMode.Regex &&
            !TransactionCategorizer.IsValidRegex(request.Pattern!.Trim()))
            return Results.BadRequest("Neplatný regulární výraz.");
        return null;
    }

    /// <summary>Copies the normalized text and amount conditions from the request onto the rule.</summary>
    private static void ApplyConditions(CategorizationRule rule, CategorizationRuleRequest request)
    {
        var hasAmount = request.MinAmount is not null || request.MaxAmount is not null;
        rule.Field = request.Field;
        rule.MatchMode = request.MatchMode;
        rule.Pattern = string.IsNullOrWhiteSpace(request.Pattern) ? null : request.Pattern.Trim();
        rule.MinAmount = hasAmount ? request.MinAmount : null;
        rule.MaxAmount = hasAmount ? request.MaxAmount : null;
        rule.AmountCurrency = hasAmount ? request.AmountCurrency!.Trim().ToUpperInvariant() : null;
    }

    /// <summary>Short human label for audit messages — the pattern, or the amount range for amount-only rules.</summary>
    private static string DescribeRule(CategorizationRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.Pattern))
            return rule.Pattern!;
        var cur = rule.AmountCurrency ?? "";
        return (rule.MinAmount, rule.MaxAmount) switch
        {
            ({ } mn, { } mx) when mn == mx => $"= {mn:0.##} {cur}",
            ({ } mn, { } mx) => $"{mn:0.##}–{mx:0.##} {cur}",
            ({ } mn, null) => $"≥ {mn:0.##} {cur}",
            (null, { } mx) => $"≤ {mx:0.##} {cur}",
            _ => "pravidlo",
        };
    }
}
