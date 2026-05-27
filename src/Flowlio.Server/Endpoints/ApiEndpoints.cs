using System.Text.Json;
using Flowlio.Application.Abstractions;
using Flowlio.Application.Mapping;
using Flowlio.Application.Statements;
using Flowlio.Domain;
using Flowlio.Infrastructure.Identity;
using Flowlio.Server.Auth;
using Flowlio.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Wolverine;
using static Flowlio.Server.Auth.MemberAuthorization;

namespace Flowlio.Server.Endpoints;

public static class ApiEndpoints
{
    public static void MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization("api");
        api.MapGet("/me", GetMe);
        api.MapGet("/accounts", GetAccounts);
        api.MapPost("/accounts", CreateAccount);
        api.MapGet("/categories", GetCategories);
        api.MapGet("/transactions", GetTransactions);
        api.MapGet("/dashboard", GetDashboard);
        api.MapPost("/import", ImportStatement).DisableAntiforgery();
        api.MapFamilyEndpoints();
        api.MapRolesEndpoints();
        api.MapFamilyManagementEndpoints();
        app.MapAdminEndpoints();
        app.MapSystemRolesEndpoints();
    }

    private static async Task<CurrentUserDto> GetMe(
        ICurrentUser currentUser, UserManager<ApplicationUser> userManager, ICurrentFamily family,
        ICurrentSystemAccess systemAccess, IConfiguration config, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        var permissions = await family.GetPermissionsAsync(ct);
        var systemPermissions = await systemAccess.GetPermissionsAsync(ct);

        var isAdmin = false;
        if (currentUser.UserId is { } userId)
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            isAdmin = user is not null && await userManager.IsInRoleAsync(user, SystemRoles.Administrator);
        }

        return new CurrentUserDto
        {
            MemberId = me.Id,
            DisplayName = me.DisplayName,
            Role = me.Role,
            IsAdmin = isAdmin,
            Permissions = permissions.ToList(),
            SystemPermissions = systemPermissions.ToList(),
            PollIntervalSeconds = config.GetValue("Auth:PollIntervalSeconds", 60),
        };
    }

    private static async Task<IReadOnlyList<BankAccountDto>> GetAccounts(
        IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        var accounts = await db.BankAccounts.Where(a => a.FamilyId == familyId).ToListAsync(ct);

        var sums = await db.Transactions
            .Where(t => t.FamilyId == familyId)
            .GroupBy(t => t.BankAccountId)
            .Select(g => new { AccountId = g.Key, Sum = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.AccountId, x => x.Sum, ct);

        var members = await db.FamilyMembers
            .Where(m => m.FamilyId == familyId)
            .Select(m => new { m.Id, m.DisplayName, m.Role })
            .ToDictionaryAsync(m => m.Id, ct);

        var cardCounts = await db.BankCards
            .Where(c => c.BankAccount!.FamilyId == familyId)
            .GroupBy(c => c.BankAccountId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        var grantCounts = await db.AccountAccesses
            .Where(g => g.BankAccount!.FamilyId == familyId)
            .GroupBy(g => g.BankAccountId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        return accounts
            .Select(a =>
            {
                var owner = a.OwnerMemberId is { } oid ? members.GetValueOrDefault(oid) : null;
                return mapper.ToDto(a) with
                {
                    CurrentBalance = a.OpeningBalance + sums.GetValueOrDefault(a.Id),
                    OwnerName = owner?.DisplayName,
                    IsChildAccount = owner?.Role == MemberRole.Child,
                    CardCount = cardCounts.GetValueOrDefault(a.Id),
                    DisponentCount = grantCounts.GetValueOrDefault(a.Id),
                };
            })
            .ToList();
    }

    private static async Task<IResult> CreateAccount(
        CreateBankAccountRequest request, IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper, CancellationToken ct)
    {
        var member = await family.RequireMemberAsync(ct);
        if (!await family.CanAsync(Permission.ManageAccounts, ct))
            return Forbidden();

        var ownerMemberId = request.OwnerMemberId ?? member.Id;

        var owner = await db.FamilyMembers
            .FirstOrDefaultAsync(m => m.Id == ownerMemberId && m.FamilyId == member.FamilyId, ct);
        if (owner is null)
            return Results.BadRequest("Neplatný vlastník účtu.");

        var account = new BankAccount
        {
            FamilyId = member.FamilyId,
            Name = request.Name,
            Bank = request.Bank,
            AccountNumber = request.AccountNumber,
            Currency = request.Currency,
            OpeningBalance = request.OpeningBalance,
            OwnerMemberId = owner.Id,
        };
        db.BankAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return Results.Ok(mapper.ToDto(account) with
        {
            CurrentBalance = account.OpeningBalance,
            OwnerName = owner.DisplayName,
            IsChildAccount = owner.Role == MemberRole.Child,
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
        ICurrentFamily family,
        IMessageBus bus,
        CancellationToken ct)
    {
        if (!await family.CanAsync(Permission.ImportStatements, ct))
            return Forbidden();

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
