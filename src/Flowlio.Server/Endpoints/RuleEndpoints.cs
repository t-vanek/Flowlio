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
        api.MapPost("/rules", CreateRule);
        api.MapPut("/rules/{id:guid}", UpdateRule);
        api.MapDelete("/rules/{id:guid}", DeleteRule);
        api.MapPost("/rules/recategorize", Recategorize);
    }

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

        return Results.Ok(rules.Select(mapper.ToDto).ToList());
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

        var rule = new CategorizationRule
        {
            FamilyId = familyId,
            Field = request.Field,
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

        rule.Field = request.Field;
        rule.Pattern = request.Pattern.Trim();
        rule.CategoryId = request.CategoryId;
        rule.Priority = request.Priority;
        rule.IsActive = request.IsActive;
        rule.UpdatedAt = DateTimeOffset.UtcNow;
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

        var pattern = rule.Pattern;
        db.CategorizationRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        // Deleting a rule does not un-categorize past transactions (we don't track which rule assigned what).
        await audit.RecordAsync("rule.delete", "CategorizationRule", id.ToString(), pattern, familyId,
            $"Smazáno pravidlo „{pattern}“", ct);
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
            .Where(r => r.FamilyId == familyId && r.IsActive)
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync(ct);
        if (rules.Count == 0)
            return 0;

        var query = db.Transactions.Where(t => t.FamilyId == familyId);
        if (onlyUncategorized)
            query = query.Where(t => t.CategoryId == null);
        var transactions = await query.ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var changed = 0;
        foreach (var t in transactions)
        {
            var match = TransactionCategorizer.Match(
                t.CounterpartyName, t.Description, t.VariableSymbol, t.CounterpartyAccount, rules);
            if (match is { } categoryId && categoryId != t.CategoryId)
            {
                t.CategoryId = categoryId;
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
        return mapper.ToDto(rule);
    }

    private static async Task<bool> CategoryBelongsToFamily(
        IAppDbContext db, Guid familyId, Guid categoryId, CancellationToken ct) =>
        await db.Categories.AnyAsync(c => c.Id == categoryId && c.FamilyId == familyId, ct);
}
