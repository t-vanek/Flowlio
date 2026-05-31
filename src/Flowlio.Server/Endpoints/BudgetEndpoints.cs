using Flowlio.Application.Abstractions;
using Flowlio.Application.Currency;
using Flowlio.Domain;
using Flowlio.Shared;
using Microsoft.EntityFrameworkCore;
using static Flowlio.Server.Auth.MemberAuthorization;

namespace Flowlio.Server.Endpoints;

/// <summary>
/// Budgets (per-category spending limits with current-period actuals) and goals (account-linked savings
/// targets). Actuals are rolled up over sub-categories and FX-converted to the family base currency.
/// </summary>
public static class BudgetEndpoints
{
    public static void MapBudgetEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/budgets", GetBudgets);
        api.MapGet("/budgets/deleted", GetDeletedBudgets);
        api.MapPost("/budgets", CreateBudget);
        api.MapPut("/budgets/{id:guid}", UpdateBudget);
        api.MapDelete("/budgets/{id:guid}", DeleteBudget);
        api.MapPost("/budgets/{id:guid}/restore", RestoreBudget);

        api.MapGet("/goals", GetGoals);
        api.MapGet("/goals/deleted", GetDeletedGoals);
        api.MapPost("/goals", CreateGoal);
        api.MapPut("/goals/{id:guid}", UpdateGoal);
        api.MapDelete("/goals/{id:guid}", DeleteGoal);
        api.MapPost("/goals/{id:guid}/restore", RestoreGoal);
    }

    // ---- Budgets ------------------------------------------------------------

    private static async Task<IResult> GetBudgets(IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ViewFinances, ct))
            return Forbidden();

        var (baseCurrency, converter) = await db.LoadCurrencyContextAsync(familyId, ct);
        var childrenByParent = await ChildrenMap(db, familyId, ct);

        var budgets = await db.Budgets
            .Include(x => x.Category)
            .Where(x => x.FamilyId == familyId)
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Each budget has its own category set (rolled up to sub-categories) and period window. Compute
        // those once, then fetch the union of relevant expense transactions in a single query and total
        // each budget's spend in memory — instead of one query per budget.
        var specs = budgets
            .Select(b => (Budget: b, Categories: Descendants(b.CategoryId, childrenByParent), Window: PeriodWindow(b.Period, today)))
            .ToList();

        var allCategoryIds = specs.SelectMany(s => s.Categories).ToHashSet();
        var minStart = specs.Count > 0 ? specs.Min(s => s.Window.Start) : today;
        var maxEnd = specs.Count > 0 ? specs.Max(s => s.Window.End) : today;

        var rows = await db.Transactions
            .Where(t => t.FamilyId == familyId && t.BankAccount!.DeletedAt == null
                        && t.Amount < 0 && t.CategoryId != null && allCategoryIds.Contains(t.CategoryId!.Value)
                        && t.BookingDate >= minStart && t.BookingDate < maxEnd)
            .Select(t => new { t.Amount, t.Currency, t.BookingDate, CategoryId = t.CategoryId!.Value })
            .ToListAsync(ct);

        var result = specs.Select(s =>
        {
            var spent = 0m;
            foreach (var r in rows)
                if (s.Categories.Contains(r.CategoryId) && r.BookingDate >= s.Window.Start && r.BookingDate < s.Window.End)
                    spent += converter.Convert(Math.Abs(r.Amount), r.Currency, baseCurrency, r.BookingDate) ?? 0m;
            return new BudgetDto
            {
                Id = s.Budget.Id,
                CategoryId = s.Budget.CategoryId,
                CategoryName = s.Budget.Category?.Name ?? "—",
                Color = s.Budget.Category?.Color,
                Period = s.Budget.Period,
                Amount = s.Budget.Amount,
                Spent = decimal.Round(spent, 2),
                Currency = baseCurrency,
                PeriodStart = s.Window.Start,
                PeriodEnd = s.Window.End,
                Version = db.GetRowVersion(s.Budget),
            };
        }).ToList();

        return Results.Ok(result.OrderByDescending(b => b.Amount == 0 ? 0 : b.Spent / b.Amount).ToList());
    }

    private static async Task<IResult> CreateBudget(
        BudgetRequest request, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == request.CategoryId && c.FamilyId == familyId, ct);
        if (category is null)
            return Results.BadRequest("Neplatná kategorie.");
        if (category.Kind != CategoryKind.Expense)
            return Results.BadRequest("Rozpočet lze nastavit jen na výdajovou kategorii.");
        if (await db.Budgets.AnyAsync(x => x.FamilyId == familyId && x.CategoryId == request.CategoryId, ct))
            return Results.BadRequest("Pro tuto kategorii už rozpočet existuje.");

        var budget = new Budget
        {
            FamilyId = familyId,
            CategoryId = request.CategoryId,
            Amount = request.Amount,
            Period = request.Period,
        };
        db.Budgets.Add(budget);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("budget.create", "Budget", budget.Id.ToString(), category.Name, familyId,
            $"Vytvořen rozpočet „{category.Name}“", ct);
        return Results.Ok(budget.Id);
    }

    private static async Task<IResult> UpdateBudget(
        Guid id, BudgetRequest request, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var budget = await db.Budgets.FirstOrDefaultAsync(x => x.Id == id && x.FamilyId == familyId, ct);
        if (budget is null)
            return Results.NotFound();

        budget.Amount = request.Amount;
        budget.Period = request.Period;
        budget.UpdatedAt = DateTimeOffset.UtcNow;
        // Optimistic concurrency: a stale Version makes SaveChanges throw, turned into HTTP 409 (Program.cs).
        db.SetOriginalRowVersion(budget, request.Version);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("budget.update", "Budget", budget.Id.ToString(), null, familyId, "Upraven rozpočet", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteBudget(
        Guid id, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var budget = await db.Budgets.FirstOrDefaultAsync(x => x.Id == id && x.FamilyId == familyId, ct);
        if (budget is null)
            return Results.NotFound();

        // Soft delete: recoverable via restore; the partial unique index lets a new budget reuse the category.
        budget.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("budget.delete", "Budget", id.ToString(), null, familyId, "Smazán rozpočet", ct);
        return Results.NoContent();
    }

    /// <summary>Soft-deleted budgets, newest first, for the "deleted budgets" panel and restore.</summary>
    private static async Task<IResult> GetDeletedBudgets(IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ViewFinances, ct))
            return Forbidden();

        var (baseCurrency, _) = await db.LoadCurrencyContextAsync(familyId, ct);
        var budgets = await db.Budgets
            .IgnoreQueryFilters()
            .Include(x => x.Category)
            .Where(x => x.FamilyId == familyId && x.DeletedAt != null)
            .OrderByDescending(x => x.DeletedAt)
            .ToListAsync(ct);

        // Actuals aren't meaningful for a deleted budget — the panel only shows the limit and a restore button.
        var result = budgets.Select(b => new BudgetDto
        {
            Id = b.Id,
            CategoryId = b.CategoryId,
            CategoryName = b.Category?.Name ?? "—",
            Color = b.Category?.Color,
            Period = b.Period,
            Amount = b.Amount,
            Spent = 0m,
            Currency = baseCurrency,
            Version = db.GetRowVersion(b),
        }).ToList();
        return Results.Ok(result);
    }

    private static async Task<IResult> RestoreBudget(
        Guid id, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var budget = await db.Budgets
            .IgnoreQueryFilters()
            .Include(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == id && x.FamilyId == familyId && x.DeletedAt != null, ct);
        if (budget is null)
            return Results.NotFound();

        // A live budget may have been created for the same category meanwhile; restoring would violate the
        // one-budget-per-category rule, so block it with a clear message rather than a raw unique violation.
        if (await db.Budgets.AnyAsync(x => x.FamilyId == familyId && x.CategoryId == budget.CategoryId, ct))
            return Results.BadRequest("Pro tuto kategorii už rozpočet existuje.");

        budget.DeletedAt = null;
        budget.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("budget.restore", "Budget", id.ToString(), budget.Category?.Name, familyId, "Obnoven rozpočet", ct);
        return Results.NoContent();
    }

    // ---- Goals --------------------------------------------------------------

    private static async Task<IResult> GetGoals(IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ViewFinances, ct))
            return Forbidden();

        var goals = await db.Goals
            .Include(g => g.BankAccount)
            .Where(g => g.FamilyId == familyId)
            .ToListAsync(ct);

        // Sum each goal account's movements in one grouped query (opening balances come from the Include
        // above), instead of two queries per goal.
        var accountIds = goals.Select(g => g.BankAccountId).Distinct().ToList();
        var movementByAccount = await db.Transactions
            .Where(t => accountIds.Contains(t.BankAccountId))
            .GroupBy(t => t.BankAccountId)
            .Select(g => new { AccountId = g.Key, Sum = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.AccountId, x => x.Sum, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = new List<GoalDto>(goals.Count);
        foreach (var goal in goals)
        {
            var balance = (goal.BankAccount?.OpeningBalance ?? 0m) + movementByAccount.GetValueOrDefault(goal.BankAccountId);
            var saved = balance - goal.BaselineAmount;
            var remaining = goal.TargetAmount - saved;
            decimal? requiredMonthly = null;
            if (goal.TargetDate is { } due && remaining > 0)
            {
                var months = MonthsUntil(today, due);
                requiredMonthly = months > 0 ? decimal.Round(remaining / months, 2) : remaining;
            }

            result.Add(new GoalDto
            {
                Id = goal.Id,
                Name = goal.Name,
                BankAccountId = goal.BankAccountId,
                AccountName = goal.BankAccount?.Name ?? "—",
                Currency = goal.BankAccount?.Currency ?? "CZK",
                TargetAmount = goal.TargetAmount,
                BaselineAmount = goal.BaselineAmount,
                Saved = saved,
                TargetDate = goal.TargetDate,
                RequiredMonthly = requiredMonthly,
                Version = db.GetRowVersion(goal),
            });
        }

        return Results.Ok(result.OrderBy(g => g.TargetDate ?? DateOnly.MaxValue).ToList());
    }

    private static async Task<IResult> CreateGoal(
        GoalRequest request, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var account = await db.BankAccounts.FirstOrDefaultAsync(a => a.Id == request.BankAccountId && a.FamilyId == familyId, ct);
        if (account is null)
            return Results.BadRequest("Neplatný účet.");

        var goal = new Goal
        {
            FamilyId = familyId,
            Name = request.Name.Trim(),
            BankAccountId = request.BankAccountId,
            TargetAmount = request.TargetAmount,
            // Default the baseline to the account's current balance, so progress counts new savings only.
            BaselineAmount = request.BaselineAmount ?? await AccountBalanceAsync(db, request.BankAccountId, ct),
            TargetDate = request.TargetDate,
        };
        db.Goals.Add(goal);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("goal.create", "Goal", goal.Id.ToString(), goal.Name, familyId,
            $"Vytvořen cíl „{goal.Name}“", ct);
        return Results.Ok(goal.Id);
    }

    private static async Task<IResult> UpdateGoal(
        Guid id, GoalRequest request, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var goal = await db.Goals.FirstOrDefaultAsync(g => g.Id == id && g.FamilyId == familyId, ct);
        if (goal is null)
            return Results.NotFound();

        goal.Name = request.Name.Trim();
        goal.TargetAmount = request.TargetAmount;
        goal.TargetDate = request.TargetDate;
        if (request.BaselineAmount is { } baseline)
            goal.BaselineAmount = baseline;
        goal.UpdatedAt = DateTimeOffset.UtcNow;
        // Optimistic concurrency: a stale Version makes SaveChanges throw, turned into HTTP 409 (Program.cs).
        db.SetOriginalRowVersion(goal, request.Version);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("goal.update", "Goal", goal.Id.ToString(), goal.Name, familyId, "Upraven cíl", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteGoal(
        Guid id, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var goal = await db.Goals.FirstOrDefaultAsync(g => g.Id == id && g.FamilyId == familyId, ct);
        if (goal is null)
            return Results.NotFound();

        // Soft delete: recoverable via restore.
        goal.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("goal.delete", "Goal", id.ToString(), goal.Name, familyId, "Smazán cíl", ct);
        return Results.NoContent();
    }

    /// <summary>Soft-deleted goals, newest first, for the "deleted goals" panel and restore.</summary>
    private static async Task<IResult> GetDeletedGoals(IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ViewFinances, ct))
            return Forbidden();

        var goals = await db.Goals
            .IgnoreQueryFilters()
            .Include(g => g.BankAccount)
            .Where(g => g.FamilyId == familyId && g.DeletedAt != null)
            .OrderByDescending(g => g.DeletedAt)
            .ToListAsync(ct);

        // Progress isn't meaningful for a deleted goal — the panel only shows the target and a restore button.
        var result = goals.Select(g => new GoalDto
        {
            Id = g.Id,
            Name = g.Name,
            BankAccountId = g.BankAccountId,
            AccountName = g.BankAccount?.Name ?? "—",
            Currency = g.BankAccount?.Currency ?? "CZK",
            TargetAmount = g.TargetAmount,
            BaselineAmount = g.BaselineAmount,
            Saved = 0m,
            TargetDate = g.TargetDate,
            Version = db.GetRowVersion(g),
        }).ToList();
        return Results.Ok(result);
    }

    private static async Task<IResult> RestoreGoal(
        Guid id, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var goal = await db.Goals
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.Id == id && g.FamilyId == familyId && g.DeletedAt != null, ct);
        if (goal is null)
            return Results.NotFound();

        goal.DeletedAt = null;
        goal.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("goal.restore", "Goal", id.ToString(), goal.Name, familyId, "Obnoven cíl", ct);
        return Results.NoContent();
    }

    // ---- Helpers ------------------------------------------------------------

    /// <summary>Maps each category to its direct children, so a budget rolls up its sub-categories' spend.</summary>
    private static async Task<ILookup<Guid, Guid>> ChildrenMap(IAppDbContext db, Guid familyId, CancellationToken ct)
    {
        var pairs = await db.Categories
            .Where(c => c.FamilyId == familyId && c.ParentId != null)
            .Select(c => new { Parent = c.ParentId!.Value, Child = c.Id })
            .ToListAsync(ct);
        return pairs.ToLookup(p => p.Parent, p => p.Child);
    }

    private static HashSet<Guid> Descendants(Guid root, ILookup<Guid, Guid> childrenByParent)
    {
        var set = new HashSet<Guid> { root };
        var queue = new Queue<Guid>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            foreach (var child in childrenByParent[queue.Dequeue()])
                if (set.Add(child))
                    queue.Enqueue(child);
        }
        return set;
    }

    private static async Task<decimal> AccountBalanceAsync(IAppDbContext db, Guid accountId, CancellationToken ct)
    {
        var opening = await db.BankAccounts.Where(a => a.Id == accountId).Select(a => a.OpeningBalance).FirstAsync(ct);
        var movements = await db.Transactions
            .Where(t => t.BankAccountId == accountId)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;
        return opening + movements;
    }

    private static (DateOnly Start, DateOnly End) PeriodWindow(BudgetPeriod period, DateOnly today) => period switch
    {
        BudgetPeriod.Weekly => WeekWindow(today),
        BudgetPeriod.Yearly => (new DateOnly(today.Year, 1, 1), new DateOnly(today.Year + 1, 1, 1)),
        _ => (new DateOnly(today.Year, today.Month, 1), new DateOnly(today.Year, today.Month, 1).AddMonths(1)),
    };

    private static (DateOnly Start, DateOnly End) WeekWindow(DateOnly today)
    {
        // ISO week: Monday-based.
        var offset = ((int)today.DayOfWeek + 6) % 7;
        var monday = today.AddDays(-offset);
        return (monday, monday.AddDays(7));
    }

    private static int MonthsUntil(DateOnly today, DateOnly target)
    {
        var months = (target.Year - today.Year) * 12 + (target.Month - today.Month);
        return Math.Max(months, 1);
    }
}
