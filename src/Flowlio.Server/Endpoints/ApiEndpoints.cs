using System.Text.Json;
using Flowlio.Application.Abstractions;
using Flowlio.Application.Mapping;
using Flowlio.Application.Statements;
using Flowlio.Domain;
using Flowlio.Infrastructure.Identity;
using Flowlio.Infrastructure.Persistence;
using Flowlio.Server.Auth;
using Flowlio.Server.Validation;
using Flowlio.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using NpgsqlTypes;
using Wolverine;
using static Flowlio.Server.Auth.MemberAuthorization;

namespace Flowlio.Server.Endpoints;

public static class ApiEndpoints
{
    public static void MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization("api").AddEndpointFilter<ValidationEndpointFilter>();
        api.MapGet("/me", GetMe);
        api.MapGet("/accounts", GetAccounts);
        api.MapGet("/accounts/archived", GetArchivedAccounts);
        api.MapPost("/accounts", CreateAccount);
        api.MapDelete("/accounts/{accountId:guid}", ArchiveAccount);
        api.MapPost("/accounts/{accountId:guid}/restore", RestoreAccount);
        api.MapGet("/categories", GetCategories);
        api.MapGet("/transactions", GetTransactions);
        api.MapPost("/transactions", CreateTransaction);
        api.MapPut("/transactions/{id:guid}", UpdateTransaction);
        api.MapDelete("/transactions/{id:guid}", DeleteTransaction);
        api.MapPost("/transactions/bulk-delete", BulkDeleteTransactions);
        api.MapPost("/transactions/bulk-categorize", BulkCategorizeTransactions);
        api.MapPost("/transactions/restore", RestoreTransactions);
        api.MapPost("/movement-batches", CreateMovementBatch);
        api.MapGet("/movement-batches", GetMovementBatches);
        api.MapPut("/movement-batches/{id:guid}", UpdateMovementBatch);
        api.MapDelete("/movement-batches/{id:guid}", DeleteMovementBatch);
        api.MapGet("/dashboard", GetDashboard);
        api.MapPost("/import", ImportStatement).DisableAntiforgery();
        api.MapRuleEndpoints();
        api.MapFamilyEndpoints();
        api.MapRolesEndpoints();
        api.MapFamilyManagementEndpoints();
        app.MapAdminEndpoints();
        app.MapSystemRolesEndpoints();
        app.MapAuditEndpoints();
    }

    private static async Task<CurrentUserDto> GetMe(
        ICurrentUser currentUser, UserManager<ApplicationUser> userManager, ICurrentFamily family,
        ICurrentSystemAccess systemAccess, IConfiguration config, CancellationToken ct)
    {
        var me = await family.RequireMemberAsync(ct);
        var permissions = await family.GetPermissionsAsync(ct);
        var systemPermissions = await systemAccess.GetPermissionsAsync(ct);

        var isAdmin = false;
        var twoFactorEnabled = false;
        DateTimeOffset? require2faBy = null;
        if (currentUser.UserId is { } userId)
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user is not null)
            {
                isAdmin = await userManager.IsInRoleAsync(user, SystemRoles.Administrator);
                twoFactorEnabled = await userManager.GetTwoFactorEnabledAsync(user);
                require2faBy = user.Require2faBy;
            }
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
            TwoFactorEnabled = twoFactorEnabled,
            Require2faByUtc = require2faBy,
        };
    }

    private static async Task<IResult> GetAccounts(
        IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ViewFinances, ct))
            return Forbidden();

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

        return Results.Ok(accounts
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
            .ToList());
    }

    private static async Task<IResult> CreateAccount(
        CreateBankAccountRequest request, IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper,
        IAuditLog audit, CancellationToken ct)
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
        await audit.RecordAsync("account.create", "BankAccount", account.Id.ToString(), account.Name, member.FamilyId,
            $"Vytvořen účet ({account.Bank})", ct);
        return Results.Ok(mapper.ToDto(account) with
        {
            CurrentBalance = account.OpeningBalance,
            OwnerName = owner.DisplayName,
            IsChildAccount = owner.Role == MemberRole.Child,
        });
    }

    private static async Task<IReadOnlyList<ArchivedAccountDto>> GetArchivedAccounts(
        IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        return await db.BankAccounts
            .IgnoreQueryFilters()
            .Where(a => a.FamilyId == familyId && a.DeletedAt != null)
            .OrderByDescending(a => a.DeletedAt)
            .Select(a => new ArchivedAccountDto
            {
                Id = a.Id,
                Name = a.Name,
                Bank = a.Bank,
                AccountNumber = a.AccountNumber,
                Currency = a.Currency,
                ArchivedAt = a.DeletedAt!.Value,
            })
            .ToListAsync(ct);
    }

    // Archiving (soft delete) keeps an account and its imported transactions for history while hiding it
    // from listings, dashboards and import; restoring brings it back.
    private static async Task<IResult> ArchiveAccount(
        Guid accountId, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var member = await family.RequireMemberAsync(ct);
        if (!await family.CanAsync(Permission.ManageAccounts, ct))
            return Forbidden();

        var account = await db.BankAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.FamilyId == member.FamilyId, ct);
        if (account is null)
            return Results.NotFound();

        account.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("account.archive", "BankAccount", account.Id.ToString(), account.Name, member.FamilyId,
            "Účet archivován", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RestoreAccount(
        Guid accountId, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var member = await family.RequireMemberAsync(ct);
        if (!await family.CanAsync(Permission.ManageAccounts, ct))
            return Forbidden();

        var account = await db.BankAccounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == accountId && a.FamilyId == member.FamilyId && a.DeletedAt != null, ct);
        if (account is null)
            return Results.NotFound();

        account.DeletedAt = null;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("account.restore", "BankAccount", account.Id.ToString(), account.Name, member.FamilyId,
            "Účet obnoven", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetCategories(
        IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ViewFinances, ct))
            return Forbidden();

        var categories = await db.Categories
            .Where(c => c.FamilyId == familyId)
            .OrderBy(c => c.Kind).ThenBy(c => c.Name)
            .ToListAsync(ct);
        return Results.Ok(categories.Select(mapper.ToDto).ToList());
    }

    private static async Task<IResult> GetTransactions(
        IAppDbContext db, ICurrentFamily family, FlowlioMapper mapper, CancellationToken ct,
        Guid? accountId = null, Guid? categoryId = null, DateOnly? dateFrom = null, DateOnly? dateTo = null,
        TransactionDirection? direction = null, string? search = null, int page = 1, int pageSize = 50)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ViewFinances, ct))
            return Forbidden();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Transactions
            .Include(t => t.Category)
            .Where(t => t.FamilyId == familyId && t.BankAccount!.DeletedAt == null);

        if (accountId is { } acc)
            query = query.Where(t => t.BankAccountId == acc);

        if (categoryId is { } cat)
            query = query.Where(t => t.CategoryId == cat);

        if (dateFrom is { } from)
            query = query.Where(t => t.BookingDate >= from);

        if (dateTo is { } to)
            query = query.Where(t => t.BookingDate <= to);

        // Income vs. expense is read from the amount sign, matching the dashboard's classification.
        if (direction is { } dir)
            query = dir == TransactionDirection.Incoming
                ? query.Where(t => t.Amount > 0)
                : query.Where(t => t.Amount < 0);

        // Full-text search (PostgreSQL): match the diacritics-folded SearchVector with a prefix
        // tsquery and, when searching, order by relevance (ts_rank) before recency.
        var tsQuery = BuildTsQuery(search);
        if (tsQuery is not null)
            query = query.Where(t =>
                EF.Property<NpgsqlTsVector>(t, "SearchVector")
                  .Matches(EF.Functions.ToTsQuery("simple", FtsFunctions.Unaccent(tsQuery))));

        var total = await query.CountAsync(ct);

        var ordered = tsQuery is not null
            ? query.OrderByDescending(t =>
                    EF.Property<NpgsqlTsVector>(t, "SearchVector")
                      .Rank(EF.Functions.ToTsQuery("simple", FtsFunctions.Unaccent(tsQuery))))
                  .ThenByDescending(t => t.BookingDate)
            : query.OrderByDescending(t => t.BookingDate).ThenByDescending(t => t.CreatedAt);

        var items = await ordered
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        return Results.Ok(new TransactionPageDto
        {
            Items = items.Select(t => mapper.ToDto(t) with { Version = db.GetRowVersion(t) }).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    // Turns free user input into a safe prefix tsquery, e.g. "kávár pizz" -> "kávár:* & pizz:*"
    // (each token is reduced to letters/digits so to_tsquery never meets an operator it can't parse;
    // accents are folded later by flowlio_immutable_unaccent, matching the stored SearchVector).
    private static string? BuildTsQuery(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return null;

        var tokens = search
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => new string(token.Where(char.IsLetterOrDigit).ToArray()))
            .Where(token => token.Length > 0)
            .Select(token => token + ":*");

        var joined = string.Join(" & ", tokens);
        return joined.Length == 0 ? null : joined;
    }

    private static async Task<IResult> GetDashboard(
        IAppDbContext db, ICurrentFamily family, IDistributedCache cache, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ViewFinances, ct))
            return Forbidden();

        var cacheKey = CacheKeys.Dashboard(familyId);
        var cached = await cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
            return Results.Ok(JsonSerializer.Deserialize<DashboardSummaryDto>(cached)!);

        var summary = await BuildDashboardAsync(db, familyId, ct);

        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(summary),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) }, ct);

        return Results.Ok(summary);
    }

    private static async Task<DashboardSummaryDto> BuildDashboardAsync(
        IAppDbContext db, Guid familyId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var nextMonth = monthStart.AddMonths(1);
        var horizon = today.AddDays(30);

        // Archived accounts are excluded from the opening balance (query filter) and from every
        // transaction roll-up below, so the dashboard reflects only live accounts.
        var openingTotal = await db.BankAccounts.Where(a => a.FamilyId == familyId).SumAsync(a => a.OpeningBalance, ct);
        var movementTotal = await db.Transactions
            .Where(t => t.FamilyId == familyId && t.BankAccount!.DeletedAt == null)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var income = await db.Transactions
            .Where(t => t.FamilyId == familyId && t.BankAccount!.DeletedAt == null
                        && t.Amount > 0 && t.BookingDate >= monthStart && t.BookingDate < nextMonth)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var expense = await db.Transactions
            .Where(t => t.FamilyId == familyId && t.BankAccount!.DeletedAt == null
                        && t.Amount < 0 && t.BookingDate >= monthStart && t.BookingDate < nextMonth)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var topCategories = await db.Transactions
            .Where(t => t.FamilyId == familyId && t.BankAccount!.DeletedAt == null
                        && t.Amount < 0 && t.CategoryId != null
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

    private static async Task<IResult> CreateTransaction(
        CreateTransactionRequest request, IAppDbContext db, ICurrentFamily family,
        FlowlioMapper mapper, IDistributedCache cache, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var account = await db.BankAccounts
            .FirstOrDefaultAsync(a => a.Id == request.BankAccountId && a.FamilyId == familyId, ct);
        if (account is null)
            return Results.BadRequest("Účet nebyl nalezen.");

        if (Validate(request.Fields) is { } error)
            return Results.BadRequest(error);
        if (!await CategoryBelongsToFamily(db, familyId, request.Fields.CategoryId, ct))
            return Results.BadRequest("Neplatná kategorie.");

        var transaction = new Transaction
        {
            FamilyId = familyId,
            BankAccountId = account.Id,
            DedupHash = DedupHasher.Unique(),
        };
        Apply(transaction, request.Fields);
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("transaction.create", "Transaction", transaction.Id.ToString(),
            TxLabel(transaction), familyId, "Vytvořen pohyb", ct);
        await InvalidateDashboard(cache, familyId, ct);

        return Results.Ok(mapper.ToDto(transaction) with { Version = db.GetRowVersion(transaction) });
    }

    private static async Task<IResult> UpdateTransaction(
        Guid id, UpdateTransactionRequest request, IAppDbContext db, ICurrentFamily family,
        FlowlioMapper mapper, IDistributedCache cache, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var transaction = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.FamilyId == familyId, ct);
        if (transaction is null)
            return Results.NotFound();

        if (Validate(request.Fields) is { } error)
            return Results.BadRequest(error);
        if (!await CategoryBelongsToFamily(db, familyId, request.Fields.CategoryId, ct))
            return Results.BadRequest("Neplatná kategorie.");

        // DedupHash stays stable: it is an import fingerprint, not derived from the editable fields.
        Apply(transaction, request.Fields);
        transaction.UpdatedAt = DateTimeOffset.UtcNow;
        // Optimistic concurrency: a stale Version makes SaveChanges throw, which the global handler
        // turns into HTTP 409 (Program.cs).
        db.SetOriginalRowVersion(transaction, request.Version);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("transaction.update", "Transaction", transaction.Id.ToString(),
            TxLabel(transaction), familyId, "Upraven pohyb", ct);
        await InvalidateDashboard(cache, familyId, ct);

        return Results.Ok(mapper.ToDto(transaction) with { Version = db.GetRowVersion(transaction) });
    }

    private static async Task<IResult> DeleteTransaction(
        Guid id, IAppDbContext db, ICurrentFamily family, IDistributedCache cache, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var transaction = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.FamilyId == familyId, ct);
        if (transaction is null)
            return Results.NotFound();

        // Soft delete: hidden everywhere but restorable (see /transactions/restore).
        transaction.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("transaction.delete", "Transaction", transaction.Id.ToString(),
            TxLabel(transaction), familyId, "Smazán pohyb", ct);
        await InvalidateDashboard(cache, familyId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> BulkDeleteTransactions(
        BulkTransactionRequest request, IAppDbContext db, ICurrentFamily family,
        IDistributedCache cache, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var ids = request.Ids.Distinct().ToList();
        if (ids.Count == 0)
            return Results.Ok(new BulkResultDto { Count = 0 });

        var transactions = await db.Transactions
            .Where(t => t.FamilyId == familyId && ids.Contains(t.Id))
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var t in transactions)
            t.DeletedAt = now;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync("transaction.bulk_delete", "Transaction", null,
            $"{transactions.Count} pohybů", familyId, $"Hromadně smazáno {transactions.Count} pohybů", ct);
        await InvalidateDashboard(cache, familyId, ct);
        return Results.Ok(new BulkResultDto { Count = transactions.Count });
    }

    private static async Task<IResult> BulkCategorizeTransactions(
        BulkCategorizeRequest request, IAppDbContext db, ICurrentFamily family,
        IDistributedCache cache, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();
        if (!await CategoryBelongsToFamily(db, familyId, request.CategoryId, ct))
            return Results.BadRequest("Neplatná kategorie.");

        var ids = request.Ids.Distinct().ToList();
        if (ids.Count == 0)
            return Results.Ok(new BulkResultDto { Count = 0 });

        var transactions = await db.Transactions
            .Where(t => t.FamilyId == familyId && ids.Contains(t.Id))
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var t in transactions)
        {
            t.CategoryId = request.CategoryId;
            t.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync("transaction.bulk_categorize", "Transaction", null,
            $"{transactions.Count} pohybů", familyId, $"Hromadně přeřazeno {transactions.Count} pohybů", ct);
        await InvalidateDashboard(cache, familyId, ct);
        return Results.Ok(new BulkResultDto { Count = transactions.Count });
    }

    private static async Task<IResult> RestoreTransactions(
        BulkTransactionRequest request, IAppDbContext db, ICurrentFamily family,
        IDistributedCache cache, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var ids = request.Ids.Distinct().ToList();
        if (ids.Count == 0)
            return Results.Ok(new BulkResultDto { Count = 0 });

        // Soft-deleted rows are hidden by the global filter, so look past it to restore them.
        var transactions = await db.Transactions
            .IgnoreQueryFilters()
            .Where(t => t.FamilyId == familyId && t.DeletedAt != null && ids.Contains(t.Id))
            .ToListAsync(ct);

        foreach (var t in transactions)
            t.DeletedAt = null;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync("transaction.restore", "Transaction", null,
            $"{transactions.Count} pohybů", familyId, $"Obnoveno {transactions.Count} pohybů", ct);
        await InvalidateDashboard(cache, familyId, ct);
        return Results.Ok(new BulkResultDto { Count = transactions.Count });
    }

    private static async Task<IResult> CreateMovementBatch(
        CreateMovementBatchRequest request, IAppDbContext db, ICurrentFamily family,
        ICurrentUser currentUser, IDistributedCache cache, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var account = await db.BankAccounts
            .FirstOrDefaultAsync(a => a.Id == request.BankAccountId && a.FamilyId == familyId, ct);
        if (account is null)
            return Results.BadRequest("Účet nebyl nalezen.");
        if (request.Movements.Count == 0)
            return Results.BadRequest("Dávka musí obsahovat alespoň jeden pohyb.");

        var categoryIds = await db.Categories
            .Where(c => c.FamilyId == familyId)
            .Select(c => c.Id)
            .ToHashSetAsync(ct);

        foreach (var movement in request.Movements)
        {
            if (Validate(movement) is { } error)
                return Results.BadRequest(error);
            if (movement.CategoryId is { } cid && !categoryIds.Contains(cid))
                return Results.BadRequest("Neplatná kategorie.");
        }

        var batch = new ImportBatch
        {
            FamilyId = familyId,
            BankAccountId = account.Id,
            Origin = BatchOrigin.Manual,
            Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim(),
            Bank = account.Bank,
            Format = ImportFormat.Csv,
            Status = ImportStatus.Completed,
            ImportedByUserId = currentUser.UserId ?? Guid.Empty,
            ImportedCount = request.Movements.Count,
        };
        db.ImportBatches.Add(batch);

        foreach (var movement in request.Movements)
        {
            var transaction = new Transaction
            {
                FamilyId = familyId,
                BankAccountId = account.Id,
                ImportBatchId = batch.Id,
                DedupHash = DedupHasher.Unique(),
            };
            Apply(transaction, movement);
            db.Transactions.Add(transaction);
        }

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("batch.create", "ImportBatch", batch.Id.ToString(),
            batch.Label, familyId, $"Vytvořena ruční dávka ({request.Movements.Count} pohybů)", ct);
        await InvalidateDashboard(cache, familyId, ct);

        return Results.Ok(new MovementBatchResultDto { BatchId = batch.Id, CreatedCount = request.Movements.Count });
    }

    private static async Task<IReadOnlyList<ImportBatchDto>> GetMovementBatches(
        IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return [];

        var batches = await db.ImportBatches
            .Where(b => b.FamilyId == familyId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);

        var accountNames = await db.BankAccounts
            .Where(a => a.FamilyId == familyId)
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        return batches.Select(b => new ImportBatchDto
        {
            Id = b.Id,
            Origin = b.Origin,
            BankAccountId = b.BankAccountId,
            AccountName = accountNames.GetValueOrDefault(b.BankAccountId),
            Name = b.Origin == BatchOrigin.Manual ? b.Label : b.FileName,
            Bank = b.Bank,
            Format = b.Format,
            Status = b.Status,
            ImportedCount = b.ImportedCount,
            DuplicateCount = b.DuplicateCount,
            CreatedAt = b.CreatedAt,
            Error = b.Error,
        }).ToList();
    }

    private static async Task<IResult> UpdateMovementBatch(
        Guid id, UpdateBatchRequest request, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var batch = await db.ImportBatches
            .FirstOrDefaultAsync(b => b.Id == id && b.FamilyId == familyId, ct);
        if (batch is null)
            return Results.NotFound();
        if (batch.Origin != BatchOrigin.Manual)
            return Results.BadRequest("Přejmenovat lze jen ručně vytvořené dávky pohybů.");

        batch.Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();
        batch.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("batch.rename", "ImportBatch", batch.Id.ToString(),
            batch.Label, familyId, "Přejmenována dávka pohybů", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteMovementBatch(
        Guid id, IAppDbContext db, ICurrentFamily family, IDistributedCache cache, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ManageTransactions, ct))
            return Forbidden();

        var batch = await db.ImportBatches
            .FirstOrDefaultAsync(b => b.Id == id && b.FamilyId == familyId, ct);
        if (batch is null)
            return Results.NotFound();

        // Removing a batch is an "undo": its transactions are soft-deleted with it rather than orphaned.
        var transactions = await db.Transactions.Where(t => t.ImportBatchId == id).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var t in transactions)
            t.DeletedAt = now;
        db.ImportBatches.Remove(batch);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("batch.delete", "ImportBatch", batch.Id.ToString(),
            batch.Label ?? batch.FileName, familyId, $"Smazána dávka ({transactions.Count} pohybů)", ct);
        await InvalidateDashboard(cache, familyId, ct);
        return Results.NoContent();
    }

    private static string? Validate(TransactionFields fields)
    {
        if (fields.BookingDate == default)
            return "Zadejte datum pohybu.";
        if (fields.Amount == 0)
            return "Částka nesmí být nulová.";
        // A blank currency defaults to CZK in Apply; anything else must be a 3-letter code so it
        // satisfies the CK_Transaction_Currency check (returns 400 here instead of a DB 500).
        var currency = fields.Currency?.Trim();
        if (!string.IsNullOrEmpty(currency) && currency.Length != 3)
            return "Měna musí mít kód o 3 znacích.";
        return null;
    }

    // Short human label for the audit log.
    private static string TxLabel(Transaction t) =>
        !string.IsNullOrWhiteSpace(t.CounterpartyName) ? t.CounterpartyName!
        : !string.IsNullOrWhiteSpace(t.Description) ? t.Description!
        : t.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " " + t.Currency;

    private static async Task<bool> CategoryBelongsToFamily(
        IAppDbContext db, Guid familyId, Guid? categoryId, CancellationToken ct) =>
        categoryId is not { } id || await db.Categories.AnyAsync(c => c.Id == id && c.FamilyId == familyId, ct);

    private static void Apply(Transaction transaction, TransactionFields fields)
    {
        transaction.BookingDate = fields.BookingDate;
        transaction.ValueDate = fields.ValueDate;
        transaction.Amount = fields.Amount;
        transaction.Currency = string.IsNullOrWhiteSpace(fields.Currency) ? "CZK" : fields.Currency.Trim().ToUpperInvariant();
        transaction.Direction = fields.Amount < 0 ? TransactionDirection.Outgoing : TransactionDirection.Incoming;
        transaction.CounterpartyName = NullIfBlank(fields.CounterpartyName);
        transaction.CounterpartyAccount = NullIfBlank(fields.CounterpartyAccount);
        transaction.VariableSymbol = NullIfBlank(fields.VariableSymbol);
        transaction.ConstantSymbol = NullIfBlank(fields.ConstantSymbol);
        transaction.SpecificSymbol = NullIfBlank(fields.SpecificSymbol);
        transaction.Description = NullIfBlank(fields.Description);
        transaction.Note = NullIfBlank(fields.Note);
        transaction.CategoryId = fields.CategoryId;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Task InvalidateDashboard(IDistributedCache cache, Guid familyId, CancellationToken ct) =>
        cache.RemoveAsync(CacheKeys.Dashboard(familyId), ct);
}
