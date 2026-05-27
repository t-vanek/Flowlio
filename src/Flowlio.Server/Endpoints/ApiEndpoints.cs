using System.Text.Json;
using Flowlio.Application.Abstractions;
using Flowlio.Application.Mapping;
using Flowlio.Application.Statements;
using Flowlio.Domain;
using Flowlio.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Wolverine;

namespace Flowlio.Server.Endpoints;

public static class ApiEndpoints
{
    public static void MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization("api");
        api.MapGet("/members", GetMembers);
        api.MapPost("/members", CreateMember);
        api.MapGet("/accounts", GetAccounts);
        api.MapPost("/accounts", CreateAccount);
        api.MapGet("/categories", GetCategories);
        api.MapGet("/transactions", GetTransactions);
        api.MapGet("/dashboard", GetDashboard);
        api.MapPost("/import", ImportStatement).DisableAntiforgery();
    }

    private static async Task<IReadOnlyList<FamilyMemberDto>> GetMembers(
        IAppDbContext db, ICurrentFamily family, ICurrentUser currentUser, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        var members = await db.FamilyMembers
            .Where(m => m.FamilyId == familyId)
            .OrderBy(m => m.Role).ThenBy(m => m.DisplayName)
            .Select(m => new FamilyMemberDto
            {
                Id = m.Id,
                DisplayName = m.DisplayName,
                Role = m.Role,
                IsCurrentUser = m.UserId == currentUser.UserId,
                AccountCount = m.Accounts.Count,
            })
            .ToListAsync(ct);
        return members;
    }

    private static async Task<IResult> CreateMember(
        CreateFamilyMemberRequest request, IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return Results.BadRequest("Jméno člena je povinné.");

        var familyId = await family.RequireAsync(ct);
        var member = new FamilyMember
        {
            FamilyId = familyId,
            DisplayName = request.DisplayName.Trim(),
            Role = request.Role,
            UserId = Guid.Empty, // not linked to a login yet
        };
        db.FamilyMembers.Add(member);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new FamilyMemberDto
        {
            Id = member.Id,
            DisplayName = member.DisplayName,
            Role = member.Role,
            IsCurrentUser = false,
            AccountCount = 0,
        });
    }

    private static async Task<IReadOnlyList<BankAccountDto>> GetAccounts(
        IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        var accounts = await db.BankAccounts
            .Include(a => a.OwnerMember)
            .Where(a => a.FamilyId == familyId)
            .ToListAsync(ct);

        var sums = await db.Transactions
            .Where(t => t.FamilyId == familyId)
            .GroupBy(t => t.BankAccountId)
            .Select(g => new { AccountId = g.Key, Sum = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.AccountId, x => x.Sum, ct);

        return accounts
            .Select(a => mapper.ToDto(a) with
            {
                CurrentBalance = a.OpeningBalance + sums.GetValueOrDefault(a.Id),
                OwnerMemberName = a.OwnerMember?.DisplayName ?? "",
            })
            .ToList();
    }

    private static async Task<IResult> CreateAccount(
        CreateBankAccountRequest request, IAppDbContext db, ICurrentFamily family, ICurrentUser currentUser,
        FlowlioMapper mapper, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);

        // The owner must be a member of this family; default to the current user's member.
        var ownerMember = request.OwnerMemberId != Guid.Empty
            ? await db.FamilyMembers.FirstOrDefaultAsync(m => m.Id == request.OwnerMemberId && m.FamilyId == familyId, ct)
            : await db.FamilyMembers.FirstOrDefaultAsync(m => m.FamilyId == familyId && m.UserId == currentUser.UserId, ct);

        if (ownerMember is null)
            return Results.BadRequest("Vlastník účtu musí být člen rodiny.");

        var account = new BankAccount
        {
            FamilyId = familyId,
            OwnerMemberId = ownerMember.Id,
            OwnerMember = ownerMember,
            Name = request.Name,
            Bank = request.Bank,
            AccountNumber = request.AccountNumber,
            Currency = request.Currency,
            OpeningBalance = request.OpeningBalance,
        };
        db.BankAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return Results.Ok(mapper.ToDto(account) with
        {
            CurrentBalance = account.OpeningBalance,
            OwnerMemberName = ownerMember.DisplayName,
        });
    }

    private static async Task<IReadOnlyList<CategoryDto>> GetCategories(
        IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        var categories = await db.Categories
            .Where(c => c.FamilyId == familyId)
            .OrderBy(c => c.Kind).ThenBy(c => c.Name)
            .ToListAsync(ct);
        return categories.Select(mapper.ToDto).ToList();
    }

    private static async Task<TransactionPageDto> GetTransactions(
        IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper, CancellationToken ct,
        Guid? accountId = null, string? search = null, int page = 1, int pageSize = 50)
    {
        var familyId = await family.RequireAsync(ct);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Transactions
            .Include(t => t.Category)
            .Where(t => t.FamilyId == familyId);

        if (accountId is { } acc)
            query = query.Where(t => t.BankAccountId == acc);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(t =>
                (t.CounterpartyName != null && EF.Functions.ILike(t.CounterpartyName, $"%{term}%")) ||
                (t.Description != null && EF.Functions.ILike(t.Description, $"%{term}%")));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(t => t.BookingDate).ThenByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        return new TransactionPageDto
        {
            Items = items.Select(mapper.ToDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    private static async Task<DashboardSummaryDto> GetDashboard(
        IAppDbContext db, ICurrentFamily family, IDistributedCache cache, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);

        var cacheKey = CacheKeys.Dashboard(familyId);
        var cached = await cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
            return JsonSerializer.Deserialize<DashboardSummaryDto>(cached)!;

        var summary = await BuildDashboardAsync(db, familyId, ct);

        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(summary),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) }, ct);

        return summary;
    }

    private static async Task<DashboardSummaryDto> BuildDashboardAsync(
        IAppDbContext db, Guid familyId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var nextMonth = monthStart.AddMonths(1);
        var horizon = today.AddDays(30);

        var openingTotal = await db.BankAccounts.Where(a => a.FamilyId == familyId).SumAsync(a => a.OpeningBalance, ct);
        var movementTotal = await db.Transactions.Where(t => t.FamilyId == familyId).SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var income = await db.Transactions
            .Where(t => t.FamilyId == familyId && t.Amount > 0 && t.BookingDate >= monthStart && t.BookingDate < nextMonth)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var expense = await db.Transactions
            .Where(t => t.FamilyId == familyId && t.Amount < 0 && t.BookingDate >= monthStart && t.BookingDate < nextMonth)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var topCategories = await db.Transactions
            .Where(t => t.FamilyId == familyId && t.Amount < 0 && t.CategoryId != null
                        && t.BookingDate >= monthStart && t.BookingDate < nextMonth)
            .GroupBy(t => new { t.Category!.Name, t.Category.Color })
            .Select(g => new CategorySpendDto { CategoryName = g.Key.Name, Color = g.Key.Color, Amount = -g.Sum(x => x.Amount) })
            .OrderByDescending(c => c.Amount)
            .Take(5)
            .ToListAsync(ct);

        var recurring = await db.RecurringPayments
            .Where(r => r.FamilyId == familyId && r.IsActive && r.NextDueDate != null && r.NextDueDate <= horizon)
            .Select(r => new UpcomingPaymentDto { Name = r.Name, Amount = r.ExpectedAmount, DueDate = r.NextDueDate })
            .ToListAsync(ct);

        var subs = await db.Subscriptions
            .Where(s => s.FamilyId == familyId && s.IsActive && s.NextRenewalDate != null && s.NextRenewalDate <= horizon)
            .Select(s => new UpcomingPaymentDto { Name = s.Name, Amount = s.Amount, DueDate = s.NextRenewalDate })
            .ToListAsync(ct);

        return new DashboardSummaryDto
        {
            TotalBalance = openingTotal + movementTotal,
            IncomeThisMonth = income,
            ExpenseThisMonth = -expense,
            NetThisMonth = income + expense,
            TopExpenseCategories = topCategories,
            Upcoming = recurring.Concat(subs).OrderBy(u => u.DueDate).Take(5).ToList(),
        };
    }

    private static async Task<IResult> ImportStatement(
        IFormFile file,
        [FromForm] Guid accountId,
        [FromForm] BankProvider bank,
        [FromForm] ImportFormat format,
        IMessageBus bus,
        CancellationToken ct)
    {
        if (file.Length == 0)
            return Results.BadRequest("Soubor je prázdný.");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        var command = new ImportStatementCommand
        {
            BankAccountId = accountId,
            Bank = bank,
            Format = format,
            FileName = file.FileName,
            Content = ms.ToArray(),
        };

        var result = await bus.InvokeAsync<ImportResultDto>(command, ct);
        return Results.Ok(result);
    }
}
