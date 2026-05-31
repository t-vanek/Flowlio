using System.Net;
using System.Net.Http.Json;
using Flowlio.Domain;
using Flowlio.Shared;

namespace Flowlio.Client.Services;

/// <summary>Outcome of a write that participates in optimistic concurrency.</summary>
public enum SaveStatus
{
    Success,
    /// <summary>The row changed since it was loaded (HTTP 409); the caller should reload.</summary>
    Conflict,
    Failed,
}

/// <summary>Current user, plus whether the API refused access because the family membership was
/// suspended (HTTP 403).</summary>
public sealed record MeResult(CurrentUserDto? User, bool Forbidden);

/// <summary>Typed wrapper over the Flowlio HTTP API used by the Blazor components.</summary>
public sealed class FlowlioApi(HttpClient http)
{
    // ---- Shared request helpers --------------------------------------------
    // These collapse the repeated "call + inspect IsSuccessStatusCode" idioms. Each preserves the exact
    // HTTP call it wraps: success → value, failure → empty/false/null. (Note GetListAsync uses
    // GetFromJsonAsync, which THROWS on a non-success status; the few endpoints that must stay quiet on
    // failure keep their own GetAsync + check below.)

    /// <summary>GET a JSON list; a null body becomes an empty list.</summary>
    private async Task<IReadOnlyList<T>> GetListAsync<T>(string url) =>
        await http.GetFromJsonAsync<List<T>>(url) ?? [];

    /// <summary>GET and read the body on success, or null on a failure status (does not throw).</summary>
    private async Task<T?> GetOrNullAsync<T>(string url) where T : class
    {
        var response = await http.GetAsync(url);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>() : null;
    }

    /// <summary>POST a JSON body, returning whether it succeeded.</summary>
    private async Task<bool> PostOkAsync(string url, object request) =>
        (await http.PostAsJsonAsync(url, request)).IsSuccessStatusCode;

    /// <summary>POST with no body (a command), returning whether it succeeded.</summary>
    private async Task<bool> PostOkAsync(string url) =>
        (await http.PostAsync(url, null)).IsSuccessStatusCode;

    /// <summary>PUT a JSON body, returning whether it succeeded.</summary>
    private async Task<bool> PutOkAsync(string url, object request) =>
        (await http.PutAsJsonAsync(url, request)).IsSuccessStatusCode;

    /// <summary>DELETE, returning whether it succeeded.</summary>
    private async Task<bool> DeleteOkAsync(string url) =>
        (await http.DeleteAsync(url)).IsSuccessStatusCode;

    /// <summary>POST a JSON body and read the response on success, or null on failure.</summary>
    private async Task<T?> PostReadAsync<T>(string url, object request) where T : class
    {
        var response = await http.PostAsJsonAsync(url, request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>() : null;
    }

    /// <summary>POST with no body and read the response on success, or null on failure.</summary>
    private async Task<T?> PostReadAsync<T>(string url) where T : class
    {
        var response = await http.PostAsync(url, null);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>() : null;
    }

    /// <summary>PUT a JSON body and read the response on success, or null on failure.</summary>
    private async Task<T?> PutReadAsync<T>(string url, object request) where T : class
    {
        var response = await http.PutAsJsonAsync(url, request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>() : null;
    }

    /// <summary>PUT a JSON body and map the result to a concurrency-aware <see cref="SaveStatus"/>.</summary>
    private async Task<SaveStatus> PutStatusAsync(string url, object request) =>
        ToStatus(await http.PutAsJsonAsync(url, request));

    private static SaveStatus ToStatus(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? SaveStatus.Success
        : response.StatusCode == HttpStatusCode.Conflict ? SaveStatus.Conflict
        : SaveStatus.Failed;

    private async Task<int> BulkAsync(string url, object request)
    {
        var response = await http.PostAsJsonAsync(url, request);
        if (!response.IsSuccessStatusCode)
            return 0;
        var result = await response.Content.ReadFromJsonAsync<BulkResultDto>();
        return result?.Count ?? 0;
    }

    // ---- Current user ------------------------------------------------------

    /// <summary>Loads the current user, distinguishing a suspended membership (403) from other
    /// failures so the UI can explain it instead of silently failing.</summary>
    public async Task<MeResult> GetMeAsync()
    {
        var response = await http.GetAsync("api/me");
        if (response.StatusCode == HttpStatusCode.Forbidden)
            return new MeResult(null, Forbidden: true);
        if (!response.IsSuccessStatusCode)
            return new MeResult(null, Forbidden: false);
        return new MeResult(await response.Content.ReadFromJsonAsync<CurrentUserDto>(), Forbidden: false);
    }

    // ---- Dashboard ---------------------------------------------------------

    public Task<DashboardSummaryDto?> GetDashboardAsync() =>
        http.GetFromJsonAsync<DashboardSummaryDto>("api/dashboard");

    public Task<IReadOnlyList<CategorySpendDto>> GetCategorySpendAsync(string period) =>
        GetListAsync<CategorySpendDto>($"api/dashboard/categories?period={period}");

    public async Task<CashFlowDto> GetCashFlowAsync(string period) =>
        await http.GetFromJsonAsync<CashFlowDto>($"api/dashboard/flow?period={period}") ?? new CashFlowDto();

    public Task<ExchangeRatesDto?> GetExchangeRatesAsync() => GetOrNullAsync<ExchangeRatesDto>("api/dashboard/rates");

    // ---- Accounts ----------------------------------------------------------

    public Task<IReadOnlyList<BankAccountDto>> GetAccountsAsync() => GetListAsync<BankAccountDto>("api/accounts");

    public Task<BankAccountDto?> CreateAccountAsync(CreateBankAccountRequest request) =>
        PostReadAsync<BankAccountDto>("api/accounts", request);

    public Task<IReadOnlyList<ArchivedAccountDto>> GetArchivedAccountsAsync() =>
        GetListAsync<ArchivedAccountDto>("api/accounts/archived");

    public Task<bool> ArchiveAccountAsync(Guid accountId) => DeleteOkAsync($"api/accounts/{accountId}");

    public Task<bool> RestoreAccountAsync(Guid accountId) => PostOkAsync($"api/accounts/{accountId}/restore");

    public Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync() => GetListAsync<CategoryDto>("api/categories");

    // --- Budgets & goals ---

    public Task<IReadOnlyList<BudgetDto>> GetBudgetsAsync() => GetListAsync<BudgetDto>("api/budgets");

    /// <summary>Soft-deleted budgets, for the "deleted budgets" panel.</summary>
    public Task<IReadOnlyList<BudgetDto>> GetDeletedBudgetsAsync() => GetListAsync<BudgetDto>("api/budgets/deleted");

    public Task<bool> CreateBudgetAsync(BudgetRequest request) => PostOkAsync("api/budgets", request);

    public Task<SaveStatus> UpdateBudgetAsync(Guid id, BudgetRequest request) =>
        PutStatusAsync($"api/budgets/{id}", request);

    public Task<bool> DeleteBudgetAsync(Guid id) => DeleteOkAsync($"api/budgets/{id}");

    public Task<bool> RestoreBudgetAsync(Guid id) => PostOkAsync($"api/budgets/{id}/restore");

    public Task<IReadOnlyList<GoalDto>> GetGoalsAsync() => GetListAsync<GoalDto>("api/goals");

    /// <summary>Soft-deleted goals, for the "deleted goals" panel.</summary>
    public Task<IReadOnlyList<GoalDto>> GetDeletedGoalsAsync() => GetListAsync<GoalDto>("api/goals/deleted");

    public Task<bool> CreateGoalAsync(GoalRequest request) => PostOkAsync("api/goals", request);

    public Task<SaveStatus> UpdateGoalAsync(Guid id, GoalRequest request) =>
        PutStatusAsync($"api/goals/{id}", request);

    public Task<bool> DeleteGoalAsync(Guid id) => DeleteOkAsync($"api/goals/{id}");

    public Task<bool> RestoreGoalAsync(Guid id) => PostOkAsync($"api/goals/{id}/restore");

    // --- Transactions ---

    public async Task<TransactionPageDto?> GetTransactionsAsync(
        Guid? accountId = null, Guid? categoryId = null, DateOnly? dateFrom = null, DateOnly? dateTo = null,
        TransactionDirection? direction = null, Guid? batchId = null, bool unbatched = false,
        string? search = null, string? sort = null, bool desc = true, int page = 1, int pageSize = 50)
    {
        var url = $"api/transactions?page={page}&pageSize={pageSize}";
        var filter = TxFilterQuery(accountId, categoryId, dateFrom, dateTo, direction, batchId, unbatched, search);
        if (filter.Length > 0)
            url += "&" + filter;
        if (!string.IsNullOrEmpty(sort))
            url += $"&sort={sort}&desc={(desc ? "true" : "false")}";
        var resp = await http.GetAsync(url);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<TransactionPageDto>() : null;
    }

    public async Task<TransactionSummaryDto> GetTransactionSummaryAsync(
        Guid? accountId = null, Guid? categoryId = null, DateOnly? dateFrom = null, DateOnly? dateTo = null,
        TransactionDirection? direction = null, Guid? batchId = null, bool unbatched = false, string? search = null)
    {
        var filter = TxFilterQuery(accountId, categoryId, dateFrom, dateTo, direction, batchId, unbatched, search);
        var url = "api/transactions/summary" + (filter.Length > 0 ? "?" + filter : "");
        var resp = await http.GetAsync(url);
        return resp.IsSuccessStatusCode
            ? await resp.Content.ReadFromJsonAsync<TransactionSummaryDto>() ?? new TransactionSummaryDto()
            : new TransactionSummaryDto();
    }

    /// <summary>Downloads the filtered set as CSV bytes (server-streamed), for the export button.</summary>
    public async Task<byte[]?> ExportTransactionsAsync(
        Guid? accountId = null, Guid? categoryId = null, DateOnly? dateFrom = null, DateOnly? dateTo = null,
        TransactionDirection? direction = null, Guid? batchId = null, bool unbatched = false, string? search = null)
    {
        var filter = TxFilterQuery(accountId, categoryId, dateFrom, dateTo, direction, batchId, unbatched, search);
        var url = "api/transactions/export" + (filter.Length > 0 ? "?" + filter : "");
        var resp = await http.GetAsync(url);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadAsByteArrayAsync() : null;
    }

    // Shared "&"-joined filter query (no leading ? or &) for the transaction list/summary/export endpoints.
    private static string TxFilterQuery(
        Guid? accountId, Guid? categoryId, DateOnly? dateFrom, DateOnly? dateTo,
        TransactionDirection? direction, Guid? batchId, bool unbatched, string? search)
    {
        var qs = new List<string>();
        if (accountId is { } a) qs.Add($"accountId={a}");
        if (categoryId is { } c) qs.Add($"categoryId={c}");
        if (dateFrom is { } f) qs.Add($"dateFrom={f:yyyy-MM-dd}");
        if (dateTo is { } t) qs.Add($"dateTo={t:yyyy-MM-dd}");
        if (direction is { } d) qs.Add($"direction={d}");
        if (batchId is { } b) qs.Add($"batchId={b}");
        if (unbatched) qs.Add("unbatched=true");
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        return string.Join("&", qs);
    }

    public async Task<ImportResultDto?> ImportStatementAsync(
        Guid accountId, BankProvider bank, ImportFormat format, string fileName, Stream content)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(content);
        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent(accountId.ToString()), "accountId");
        form.Add(new StringContent(((int)bank).ToString()), "bank");
        form.Add(new StringContent(((int)format).ToString()), "format");

        var response = await http.PostAsync("api/import", form);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ImportResultDto>()
            : null;
    }

    // --- Open Banking (Enable Banking) ---

    public Task<EnableBankingCredentialStatusDto?> GetBankCredentialStatusAsync() =>
        http.GetFromJsonAsync<EnableBankingCredentialStatusDto>("api/bank-connections/credentials");

    public Task<bool> SaveBankCredentialAsync(SaveEnableBankingCredentialRequest request) =>
        PutOkAsync("api/bank-connections/credentials", request);

    public Task<bool> DeleteBankCredentialAsync() => DeleteOkAsync("api/bank-connections/credentials");

    public async Task<IReadOnlyList<BankAspspDto>> GetAvailableBanksAsync(string country)
    {
        // Stays quiet on failure (returns []) rather than throwing, since the bank list is best-effort.
        var response = await http.GetAsync($"api/bank-connections/banks?country={Uri.EscapeDataString(country)}");
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<List<BankAspspDto>>() ?? []
            : [];
    }

    public Task<IReadOnlyList<BankConnectionDto>> GetBankConnectionsAsync() =>
        GetListAsync<BankConnectionDto>("api/bank-connections");

    public Task<StartBankConnectionResultDto?> StartBankConnectionAsync(StartBankConnectionRequest request) =>
        PostReadAsync<StartBankConnectionResultDto>("api/bank-connections", request);

    public Task<ImportResultDto?> SyncBankConnectionAsync(Guid id) =>
        PostReadAsync<ImportResultDto>($"api/bank-connections/{id}/sync");

    public Task<bool> DisconnectBankAsync(Guid id) => DeleteOkAsync($"api/bank-connections/{id}");

    // --- Manual transactions & movement batches ---

    public Task<TransactionDto?> CreateTransactionAsync(CreateTransactionRequest request) =>
        PostReadAsync<TransactionDto>("api/transactions", request);

    public Task<TransactionDto?> UpdateTransactionAsync(Guid id, UpdateTransactionRequest request) =>
        PutReadAsync<TransactionDto>($"api/transactions/{id}", request);

    public Task<bool> DeleteTransactionAsync(Guid id) => DeleteOkAsync($"api/transactions/{id}");

    public Task<int> BulkDeleteTransactionsAsync(IReadOnlyList<Guid> ids) =>
        BulkAsync("api/transactions/bulk-delete", new BulkTransactionRequest { Ids = ids });

    /// <summary>Uncategorized transactions grouped by counterparty, for the triage inbox.</summary>
    public Task<IReadOnlyList<UncategorizedGroupDto>> GetUncategorizedAsync() =>
        GetListAsync<UncategorizedGroupDto>("api/transactions/uncategorized");

    public Task<int> BulkCategorizeAsync(IReadOnlyList<Guid> ids, Guid? categoryId) =>
        BulkAsync("api/transactions/bulk-categorize", new BulkCategorizeRequest { Ids = ids, CategoryId = categoryId });

    public Task<int> RestoreTransactionsAsync(IReadOnlyList<Guid> ids) =>
        BulkAsync("api/transactions/restore", new BulkTransactionRequest { Ids = ids });

    public Task<MovementBatchResultDto?> CreateMovementBatchAsync(CreateMovementBatchRequest request) =>
        PostReadAsync<MovementBatchResultDto>("api/movement-batches", request);

    public Task<IReadOnlyList<ImportBatchDto>> GetMovementBatchesAsync() =>
        GetListAsync<ImportBatchDto>("api/movement-batches");

    public Task<bool> UpdateMovementBatchAsync(Guid id, string? label) =>
        PutOkAsync($"api/movement-batches/{id}", new UpdateBatchRequest { Label = label });

    public Task<bool> DeleteMovementBatchAsync(Guid id) => DeleteOkAsync($"api/movement-batches/{id}");

    // --- Categorization rules ---

    public Task<IReadOnlyList<CategorizationRuleDto>> GetRulesAsync() =>
        GetListAsync<CategorizationRuleDto>("api/rules");

    /// <summary>Rules learned from repeated manual categorization, offered for one-click confirmation.</summary>
    public Task<IReadOnlyList<RuleSuggestionDto>> GetRuleSuggestionsAsync() =>
        GetListAsync<RuleSuggestionDto>("api/rules/suggestions");

    /// <summary>Permanently dismisses a learned suggestion so it isn't offered again.</summary>
    public Task<bool> DismissRuleSuggestionAsync(string pattern, Guid categoryId) =>
        PostOkAsync("api/rules/suggestions/dismiss",
            new RuleSuggestionDismissRequest { Pattern = pattern, CategoryId = categoryId });

    /// <summary>Dry-run impact of a rule before saving (match count + sample transactions).</summary>
    public Task<RulePreviewDto?> PreviewRuleAsync(CategorizationRuleRequest request) =>
        PostReadAsync<RulePreviewDto>("api/rules/preview", request);

    public Task<CategorizationRuleDto?> CreateRuleAsync(CategorizationRuleRequest request) =>
        PostReadAsync<CategorizationRuleDto>("api/rules", request);

    public Task<CategorizationRuleDto?> UpdateRuleAsync(Guid id, CategorizationRuleRequest request) =>
        PutReadAsync<CategorizationRuleDto>($"api/rules/{id}", request);

    /// <summary>Persists a new priority order (highest first) for the given rules.</summary>
    public Task<bool> ReorderRulesAsync(IReadOnlyList<Guid> orderedIds) =>
        PostOkAsync("api/rules/reorder", new ReorderRulesRequest { OrderedIds = orderedIds });

    /// <summary>Exports the visible rules as portable definitions (category/account by name).</summary>
    public Task<IReadOnlyList<RuleExportDto>> ExportRulesAsync() =>
        GetListAsync<RuleExportDto>("api/rules/export");

    public Task<RuleImportResultDto?> ImportRulesAsync(IReadOnlyList<RuleExportDto> items) =>
        PostReadAsync<RuleImportResultDto>("api/rules/import", items);

    public Task<bool> DeleteRuleAsync(Guid id) => DeleteOkAsync($"api/rules/{id}");

    /// <summary>Soft-deleted rules, for the "deleted rules" panel.</summary>
    public Task<IReadOnlyList<CategorizationRuleDto>> GetDeletedRulesAsync() =>
        GetListAsync<CategorizationRuleDto>("api/rules/deleted");

    public Task<bool> RestoreRuleAsync(Guid id) => PostOkAsync($"api/rules/{id}/restore");

    /// <summary>Re-runs the family's rules over existing transactions; returns how many were categorized.</summary>
    public Task<int> RecategorizeAsync(bool onlyUncategorized) =>
        BulkAsync("api/rules/recategorize", new RecategorizeRequest { OnlyUncategorized = onlyUncategorized });

    // --- Family members & invitations ---

    public Task<IReadOnlyList<FamilyMemberDto>> GetMembersAsync() => GetListAsync<FamilyMemberDto>("api/members");

    public Task<CreateMemberResultDto?> CreateMemberAsync(CreateMemberRequest request) =>
        PostReadAsync<CreateMemberResultDto>("api/members", request);

    public Task<SaveStatus> UpdateMemberAsync(Guid memberId, UpdateMemberRequest request) =>
        PutStatusAsync($"api/members/{memberId}", request);

    public Task<bool> DeleteMemberAsync(Guid memberId) => DeleteOkAsync($"api/members/{memberId}");

    public Task<bool> SetMemberActiveAsync(Guid memberId, bool active) =>
        PostOkAsync($"api/members/{memberId}/{(active ? "activate" : "deactivate")}");

    public Task<InvitationDto?> ReinviteMemberAsync(Guid memberId) =>
        PostReadAsync<InvitationDto>($"api/members/{memberId}/invite");

    public async Task<IReadOnlyList<InvitationDto>> GetInvitationsAsync()
    {
        // Quiet on failure (returns []) — invitations are a secondary panel that shouldn't error the page.
        var response = await http.GetAsync("api/invitations");
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<List<InvitationDto>>() ?? []
            : [];
    }

    public Task<bool> RevokeInvitationAsync(Guid invitationId) =>
        PostOkAsync($"api/invitations/{invitationId}/revoke");

    // --- Per-account access (disponents) ---

    public Task<AccountAccessOverviewDto?> GetAccountAccessAsync(Guid accountId) =>
        http.GetFromJsonAsync<AccountAccessOverviewDto>($"api/accounts/{accountId}/access");

    public Task<AccountAccessDto?> GrantAccessAsync(Guid accountId, GrantAccessRequest request) =>
        PostReadAsync<AccountAccessDto>($"api/accounts/{accountId}/access", request);

    public Task<bool> RevokeAccessAsync(Guid accountId, Guid memberId) =>
        DeleteOkAsync($"api/accounts/{accountId}/access/{memberId}");

    // --- Cards ---

    public Task<IReadOnlyList<BankCardDto>> GetCardsAsync(Guid accountId) =>
        GetListAsync<BankCardDto>($"api/accounts/{accountId}/cards");

    public Task<BankCardDto?> CreateCardAsync(Guid accountId, CreateCardRequest request) =>
        PostReadAsync<BankCardDto>($"api/accounts/{accountId}/cards", request);

    public Task<SaveStatus> UpdateCardAsync(Guid cardId, UpdateCardRequest request) =>
        PutStatusAsync($"api/cards/{cardId}", request);

    public Task<bool> DeleteCardAsync(Guid cardId) => DeleteOkAsync($"api/cards/{cardId}");

    // --- Roles & permissions (per family) ---

    public Task<FamilyRolesDto?> GetRolesAsync() => http.GetFromJsonAsync<FamilyRolesDto>("api/roles");

    public Task<bool> UpdateRoleAsync(MemberRole role, IReadOnlyList<Permission> permissions) =>
        PutOkAsync($"api/roles/{role}", new UpdateRolePermissionsRequest { Permissions = permissions });

    // --- Family management ---

    public Task<FamilyDto?> GetFamilyAsync() => http.GetFromJsonAsync<FamilyDto>("api/family");

    public Task<SaveStatus> UpdateFamilyAsync(UpdateFamilyRequest request) =>
        PutStatusAsync("api/family", request);

    public Task<bool> TransferOwnershipAsync(Guid newOwnerMemberId) =>
        PostOkAsync("api/family/transfer-ownership", new TransferOwnershipRequest { NewOwnerMemberId = newOwnerMemberId });

    public async Task<bool> DeleteFamilyAsync(string confirmName)
    {
        // DELETE with a body needs a hand-built request message (DeleteAsync takes no content).
        var request = new HttpRequestMessage(HttpMethod.Delete, "api/family")
        {
            Content = JsonContent.Create(new DeleteFamilyRequest { ConfirmName = confirmName }),
        };
        return (await http.SendAsync(request)).IsSuccessStatusCode;
    }

    // --- System administration (admin only) ---

    public Task<AdminUserPageDto?> GetAdminUsersAsync(int page = 1, int pageSize = 25, string? search = null, string? status = null)
    {
        var url = $"api/admin/users?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
            url += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(status))
            url += $"&status={Uri.EscapeDataString(status)}";
        return GetOrNullAsync<AdminUserPageDto>(url);
    }

    public Task<bool> CreateUserAsync(CreateUserRequest request) => PostOkAsync("api/admin/users", request);

    public Task<bool> SetUserRolesAsync(Guid userId, IReadOnlyList<string> roleNames) =>
        PutOkAsync($"api/admin/users/{userId}/roles", new SetUserRolesRequest { RoleNames = roleNames });

    // --- System roles & permissions ---

    public Task<SystemRolesDto?> GetSystemRolesAsync() => http.GetFromJsonAsync<SystemRolesDto>("api/admin/system-roles");

    public Task<bool> CreateSystemRoleAsync(string name) =>
        PostOkAsync("api/admin/system-roles", new CreateSystemRoleRequest { Name = name });

    public Task<bool> RenameSystemRoleAsync(Guid roleId, string name) =>
        PutOkAsync($"api/admin/system-roles/{roleId}", new RenameSystemRoleRequest { Name = name });

    public Task<bool> SetSystemRolePermissionsAsync(Guid roleId, IReadOnlyList<SystemPermission> permissions) =>
        PutOkAsync($"api/admin/system-roles/{roleId}/permissions", new UpdateSystemRolePermissionsRequest { Permissions = permissions });

    public Task<bool> DeleteSystemRoleAsync(Guid roleId) => DeleteOkAsync($"api/admin/system-roles/{roleId}");

    // --- Audit log ---

    public Task<AuditPageDto?> GetAuditAsync(
        string? search = null, string? action = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null,
        int page = 1) =>
        GetOrNullAsync<AuditPageDto>(BuildAuditUrl("api/admin/audit", search, action, from, to, page));

    public async Task<IReadOnlyList<string>> GetAuditActionsAsync()
    {
        // Quiet on failure (returns []) — the action filter is optional sugar.
        var response = await http.GetAsync("api/admin/audit/actions");
        if (!response.IsSuccessStatusCode) return [];
        return await response.Content.ReadFromJsonAsync<List<string>>() ?? [];
    }

    /// <summary>Downloads the audit log as CSV (filtered by the same query as the listing).</summary>
    public async Task<(byte[] Bytes, string FileName)?> ExportAuditCsvAsync(
        string? search = null, string? action = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var response = await http.GetAsync(BuildAuditUrl("api/admin/audit/export", search, action, from, to, page: null));
        if (!response.IsSuccessStatusCode) return null;
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? $"flowlio-audit-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
        return (bytes, fileName);
    }

    private static string BuildAuditUrl(string path, string? search, string? action, DateTimeOffset? from, DateTimeOffset? to, int? page)
    {
        var qs = new List<string>();
        if (page is int p) qs.Add($"page={p}");
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        if (!string.IsNullOrWhiteSpace(action)) qs.Add($"action={Uri.EscapeDataString(action)}");
        if (from is { } f) qs.Add($"from={Uri.EscapeDataString(f.UtcDateTime.ToString("O"))}");
        if (to is { } t)   qs.Add($"to={Uri.EscapeDataString(t.UtcDateTime.ToString("O"))}");
        return qs.Count == 0 ? path : $"{path}?{string.Join('&', qs)}";
    }

    public Task<bool> LockUserAsync(Guid userId, int minutes) =>
        PostOkAsync($"api/admin/users/{userId}/lock", new LockUserRequest { Minutes = minutes });

    public Task<bool> BlockUserAsync(Guid userId) => PostOkAsync($"api/admin/users/{userId}/block");

    public Task<bool> RestoreUserAsync(Guid userId) => PostOkAsync($"api/admin/users/{userId}/restore");

    public Task<bool> SetUserPasswordAsync(Guid userId, AdminSetPasswordRequest request) =>
        PostOkAsync($"api/admin/users/{userId}/password", request);

    public Task<bool> ForcePasswordChangeAsync(Guid userId) =>
        PostOkAsync($"api/admin/users/{userId}/force-password-change");

    public Task<bool> DisableUser2faAsync(Guid userId) => PostOkAsync($"api/admin/users/{userId}/disable-2fa");

    public Task<bool> Require2faAsync(Guid userId, DateTimeOffset? deadlineUtc) =>
        PostOkAsync($"api/admin/users/{userId}/require-2fa-by", new Require2faRequest { DeadlineUtc = deadlineUtc });

    public Task<bool> ForceLogoutAsync(Guid userId) => PostOkAsync($"api/admin/users/{userId}/force-logout");

    public Task<bool> DeleteUserAsync(Guid userId) => DeleteOkAsync($"api/admin/users/{userId}");

    public Task<AdminUserPageDto?> GetDeletedUsersAsync(int page = 1, int pageSize = 25, string? search = null)
    {
        var url = $"api/admin/users/deleted?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
            url += $"&search={Uri.EscapeDataString(search)}";
        return GetOrNullAsync<AdminUserPageDto>(url);
    }

    public Task<bool> UndeleteUserAsync(Guid userId) => PostOkAsync($"api/admin/users/{userId}/undelete");

    public Task<bool> PurgeUserAsync(Guid userId) => DeleteOkAsync($"api/admin/users/{userId}/purge");
}
