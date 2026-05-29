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

    /// <summary>Suggests rules learned from repeated manual categorization: counterparties a person has
    /// filed into the same category at least <see cref="SuggestionThreshold"/> times and which the current
    /// active rules don't already categorize that way. The user accepts (creates a rule) or dismisses.</summary>
    private static async Task<IResult> GetSuggestions(
        IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var manual = await db.Transactions
            .Where(t => t.FamilyId == familyId
                && t.CategorySource == CategorySource.Manual
                && t.CategoryId != null
                && t.CounterpartyName != null
                && t.CounterpartyName != "")
            .Select(t => new { Name = t.CounterpartyName!, Direction = t.Direction, CategoryId = t.CategoryId!.Value })
            .ToListAsync(ct);
        if (manual.Count == 0)
            return Results.Ok(new List<RuleSuggestionDto>());

        var dismissed = (await db.RuleSuggestionDismissals
                .Where(d => d.FamilyId == familyId)
                .Select(d => new { d.CounterpartyKey, d.CategoryId })
                .ToListAsync(ct))
            .Select(d => (d.CounterpartyKey, d.CategoryId))
            .ToHashSet();

        var rules = await db.CategorizationRules
            .Include(r => r.Category)
            .Where(r => r.FamilyId == familyId && r.IsActive)
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync(ct);

        var categoryNames = await db.Categories
            .Where(c => c.FamilyId == familyId)
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
                var direction = g.First().Direction;
                return (Pattern: pattern, g.Key.CategoryId, Count: g.Count(), Direction: direction);
            })
            // Skip merchants the active rules already file into this same category.
            .Where(s => TransactionCategorizer.Match(s.Pattern, null, null, null, s.Direction, rules) != s.CategoryId)
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
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var rules = await db.CategorizationRules
            .Include(r => r.Category)
            .Where(r => r.FamilyId == familyId)
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync(ct);

        return Results.Ok(rules.Select(r => mapper.ToDto(r) with { Version = db.GetRowVersion(r) }).ToList());
    }

    /// <summary>Soft-deleted rules, newest first, for the "deleted rules" panel and restore.</summary>
    private static async Task<IResult> GetDeletedRules(
        IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var rules = await db.CategorizationRules
            .IgnoreQueryFilters()
            .Include(r => r.Category)
            .Where(r => r.FamilyId == familyId && r.DeletedAt != null)
            .OrderByDescending(r => r.DeletedAt)
            .ToListAsync(ct);

        return Results.Ok(rules.Select(r => mapper.ToDto(r) with { Version = db.GetRowVersion(r) }).ToList());
    }

    private static async Task<IResult> CreateRule(
        CategorizationRuleRequest request, IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper,
        IDistributedCache cache, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();
        if (!await CategoryBelongsToFamily(db, familyId, request.CategoryId, ct))
            return Results.BadRequest("Neplatná kategorie.");
        if (InvalidRegex(request))
            return Results.BadRequest("Neplatný regulární výraz.");

        var rule = new CategorizationRule
        {
            FamilyId = familyId,
            Field = request.Field,
            MatchMode = request.MatchMode,
            Pattern = request.Pattern.Trim(),
            CategoryId = request.CategoryId,
            Priority = request.Priority,
            IsActive = request.IsActive,
        };
        db.CategorizationRules.Add(rule);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("rule.create", "CategorizationRule", rule.Id.ToString(), rule.Pattern, familyId,
            $"Vytvořeno pravidlo „{rule.Pattern}“", ct);

        // Backfill uncategorized transactions so the new rule applies to history too.
        await RecategorizeAsync(db, cache, familyId, onlyUncategorized: true, ct);

        return Results.Ok(await LoadDtoAsync(db, mapper, rule.Id, ct));
    }

    private static async Task<IResult> UpdateRule(
        Guid id, CategorizationRuleRequest request, IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper,
        IDistributedCache cache, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var rule = await db.CategorizationRules
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == familyId, ct);
        if (rule is null)
            return Results.NotFound();
        if (!await CategoryBelongsToFamily(db, familyId, request.CategoryId, ct))
            return Results.BadRequest("Neplatná kategorie.");
        if (InvalidRegex(request))
            return Results.BadRequest("Neplatný regulární výraz.");

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
        await audit.RecordAsync("rule.update", "CategorizationRule", rule.Id.ToString(), rule.Pattern, familyId,
            $"Upraveno pravidlo „{rule.Pattern}“", ct);

        await RecategorizeAsync(db, cache, familyId, onlyUncategorized: true, ct);

        return Results.Ok(await LoadDtoAsync(db, mapper, rule.Id, ct));
    }

    private static async Task<IResult> DeleteRule(
        Guid id, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var rule = await db.CategorizationRules
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == familyId, ct);
        if (rule is null)
            return Results.NotFound();

        // Soft delete: the rule stops categorizing but is recoverable via restore. Already-categorized
        // transactions keep their category (we don't track which rule assigned what).
        rule.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("rule.delete", "CategorizationRule", id.ToString(), rule.Pattern, familyId,
            $"Smazáno pravidlo „{rule.Pattern}“", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RestoreRule(
        Guid id, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var rule = await db.CategorizationRules
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == familyId && r.DeletedAt != null, ct);
        if (rule is null)
            return Results.NotFound();

        rule.DeletedAt = null;
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("rule.restore", "CategorizationRule", id.ToString(), rule.Pattern, familyId,
            $"Obnoveno pravidlo „{rule.Pattern}“", ct);
        return Results.NoContent();
    }

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
        var rules = await db.CategorizationRules
            .Include(r => r.Category)
            .Where(r => r.FamilyId == familyId && r.IsActive)
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync(ct);
        if (rules.Count == 0)
            return 0;

        // Never touch human choices: rules only fill in or replace rule-assigned/empty categories.
        var query = db.Transactions.Where(t => t.FamilyId == familyId && t.CategorySource != CategorySource.Manual);
        if (onlyUncategorized)
            query = query.Where(t => t.CategoryId == null);
        var transactions = await query.ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var changed = 0;
        foreach (var t in transactions)
        {
            var match = TransactionCategorizer.Match(
                t.CounterpartyName, t.Description, t.VariableSymbol, t.CounterpartyAccount, t.Direction, rules);
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
        IAppDbContext db, FlowlioMapper mapper, Guid ruleId, CancellationToken ct)
    {
        var rule = await db.CategorizationRules
            .Include(r => r.Category)
            .FirstAsync(r => r.Id == ruleId, ct);
        return mapper.ToDto(rule) with { Version = db.GetRowVersion(rule) };
    }

    private static async Task<bool> CategoryBelongsToFamily(
        IAppDbContext db, Guid familyId, Guid categoryId, CancellationToken ct) =>
        await db.Categories.AnyAsync(c => c.Id == categoryId && c.FamilyId == familyId, ct);

    /// <summary>True when the rule uses regex mode but the pattern doesn't compile, so we reject it early
    /// instead of silently never matching at import time.</summary>
    private static bool InvalidRegex(CategorizationRuleRequest request) =>
        request.MatchMode == RuleMatchMode.Regex && !TransactionCategorizer.IsValidRegex(request.Pattern.Trim());
}
