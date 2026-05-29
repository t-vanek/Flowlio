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

    public Task<DashboardSummaryDto?> GetDashboardAsync() =>
        http.GetFromJsonAsync<DashboardSummaryDto>("api/dashboard");

    public async Task<IReadOnlyList<BankAccountDto>> GetAccountsAsync() =>
        await http.GetFromJsonAsync<List<BankAccountDto>>("api/accounts") ?? [];

    public async Task<BankAccountDto?> CreateAccountAsync(CreateBankAccountRequest request)
    {
        var response = await http.PostAsJsonAsync("api/accounts", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<BankAccountDto>()
            : null;
    }

    public async Task<IReadOnlyList<ArchivedAccountDto>> GetArchivedAccountsAsync() =>
        await http.GetFromJsonAsync<List<ArchivedAccountDto>>("api/accounts/archived") ?? [];

    public async Task<bool> ArchiveAccountAsync(Guid accountId) =>
        (await http.DeleteAsync($"api/accounts/{accountId}")).IsSuccessStatusCode;

    public async Task<bool> RestoreAccountAsync(Guid accountId) =>
        (await http.PostAsync($"api/accounts/{accountId}/restore", null)).IsSuccessStatusCode;

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync() =>
        await http.GetFromJsonAsync<List<CategoryDto>>("api/categories") ?? [];

    // --- Budgets & goals ---

    public async Task<IReadOnlyList<BudgetDto>> GetBudgetsAsync() =>
        await http.GetFromJsonAsync<List<BudgetDto>>("api/budgets") ?? [];

    public async Task<bool> CreateBudgetAsync(BudgetRequest request) =>
        (await http.PostAsJsonAsync("api/budgets", request)).IsSuccessStatusCode;

    public async Task<bool> UpdateBudgetAsync(Guid id, BudgetRequest request) =>
        (await http.PutAsJsonAsync($"api/budgets/{id}", request)).IsSuccessStatusCode;

    public async Task<bool> DeleteBudgetAsync(Guid id) =>
        (await http.DeleteAsync($"api/budgets/{id}")).IsSuccessStatusCode;

    public async Task<IReadOnlyList<GoalDto>> GetGoalsAsync() =>
        await http.GetFromJsonAsync<List<GoalDto>>("api/goals") ?? [];

    public async Task<bool> CreateGoalAsync(GoalRequest request) =>
        (await http.PostAsJsonAsync("api/goals", request)).IsSuccessStatusCode;

    public async Task<bool> UpdateGoalAsync(Guid id, GoalRequest request) =>
        (await http.PutAsJsonAsync($"api/goals/{id}", request)).IsSuccessStatusCode;

    public async Task<bool> DeleteGoalAsync(Guid id) =>
        (await http.DeleteAsync($"api/goals/{id}")).IsSuccessStatusCode;

    public async Task<TransactionPageDto?> GetTransactionsAsync(
        Guid? accountId = null, Guid? categoryId = null, DateOnly? dateFrom = null, DateOnly? dateTo = null,
        TransactionDirection? direction = null, string? search = null, int page = 1, int pageSize = 50)
    {
        var url = $"api/transactions?page={page}&pageSize={pageSize}";
        if (accountId is { } id)
            url += $"&accountId={id}";
        if (categoryId is { } cid)
            url += $"&categoryId={cid}";
        if (dateFrom is { } from)
            url += $"&dateFrom={from:yyyy-MM-dd}";
        if (dateTo is { } to)
            url += $"&dateTo={to:yyyy-MM-dd}";
        if (direction is { } dir)
            url += $"&direction={dir}";
        if (!string.IsNullOrWhiteSpace(search))
            url += $"&search={Uri.EscapeDataString(search)}";
        return await http.GetFromJsonAsync<TransactionPageDto>(url);
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

    // --- Manual transactions & movement batches ---

    public async Task<TransactionDto?> CreateTransactionAsync(CreateTransactionRequest request)
    {
        var response = await http.PostAsJsonAsync("api/transactions", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<TransactionDto>()
            : null;
    }

    public async Task<TransactionDto?> UpdateTransactionAsync(Guid id, UpdateTransactionRequest request)
    {
        var response = await http.PutAsJsonAsync($"api/transactions/{id}", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<TransactionDto>()
            : null;
    }

    public async Task<bool> DeleteTransactionAsync(Guid id) =>
        (await http.DeleteAsync($"api/transactions/{id}")).IsSuccessStatusCode;

    public Task<int> BulkDeleteTransactionsAsync(IReadOnlyList<Guid> ids) =>
        BulkAsync("api/transactions/bulk-delete", new BulkTransactionRequest { Ids = ids });

    /// <summary>Uncategorized transactions grouped by counterparty, for the triage inbox.</summary>
    public async Task<IReadOnlyList<UncategorizedGroupDto>> GetUncategorizedAsync() =>
        await http.GetFromJsonAsync<List<UncategorizedGroupDto>>("api/transactions/uncategorized") ?? [];

    public Task<int> BulkCategorizeAsync(IReadOnlyList<Guid> ids, Guid? categoryId) =>
        BulkAsync("api/transactions/bulk-categorize", new BulkCategorizeRequest { Ids = ids, CategoryId = categoryId });

    public Task<int> RestoreTransactionsAsync(IReadOnlyList<Guid> ids) =>
        BulkAsync("api/transactions/restore", new BulkTransactionRequest { Ids = ids });

    private async Task<int> BulkAsync(string url, object request)
    {
        var response = await http.PostAsJsonAsync(url, request);
        if (!response.IsSuccessStatusCode)
            return 0;
        var result = await response.Content.ReadFromJsonAsync<BulkResultDto>();
        return result?.Count ?? 0;
    }

    public async Task<MovementBatchResultDto?> CreateMovementBatchAsync(CreateMovementBatchRequest request)
    {
        var response = await http.PostAsJsonAsync("api/movement-batches", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<MovementBatchResultDto>()
            : null;
    }

    public async Task<IReadOnlyList<ImportBatchDto>> GetMovementBatchesAsync() =>
        await http.GetFromJsonAsync<List<ImportBatchDto>>("api/movement-batches") ?? [];

    public async Task<bool> UpdateMovementBatchAsync(Guid id, string? label) =>
        (await http.PutAsJsonAsync($"api/movement-batches/{id}", new UpdateBatchRequest { Label = label })).IsSuccessStatusCode;

    public async Task<bool> DeleteMovementBatchAsync(Guid id) =>
        (await http.DeleteAsync($"api/movement-batches/{id}")).IsSuccessStatusCode;

    // --- Categorization rules ---

    public async Task<IReadOnlyList<CategorizationRuleDto>> GetRulesAsync() =>
        await http.GetFromJsonAsync<List<CategorizationRuleDto>>("api/rules") ?? [];

    /// <summary>Rules learned from repeated manual categorization, offered for one-click confirmation.</summary>
    public async Task<IReadOnlyList<RuleSuggestionDto>> GetRuleSuggestionsAsync() =>
        await http.GetFromJsonAsync<List<RuleSuggestionDto>>("api/rules/suggestions") ?? [];

    /// <summary>Permanently dismisses a learned suggestion so it isn't offered again.</summary>
    public async Task<bool> DismissRuleSuggestionAsync(string pattern, Guid categoryId)
    {
        var response = await http.PostAsJsonAsync("api/rules/suggestions/dismiss",
            new RuleSuggestionDismissRequest { Pattern = pattern, CategoryId = categoryId });
        return response.IsSuccessStatusCode;
    }

    /// <summary>Dry-run impact of a rule before saving (match count + sample transactions).</summary>
    public async Task<RulePreviewDto?> PreviewRuleAsync(CategorizationRuleRequest request)
    {
        var response = await http.PostAsJsonAsync("api/rules/preview", request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<RulePreviewDto>() : null;
    }

    public async Task<CategorizationRuleDto?> CreateRuleAsync(CategorizationRuleRequest request)
    {
        var response = await http.PostAsJsonAsync("api/rules", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<CategorizationRuleDto>()
            : null;
    }

    public async Task<CategorizationRuleDto?> UpdateRuleAsync(Guid id, CategorizationRuleRequest request)
    {
        var response = await http.PutAsJsonAsync($"api/rules/{id}", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<CategorizationRuleDto>()
            : null;
    }

    /// <summary>Persists a new priority order (highest first) for the given rules.</summary>
    public async Task<bool> ReorderRulesAsync(IReadOnlyList<Guid> orderedIds) =>
        (await http.PostAsJsonAsync("api/rules/reorder", new ReorderRulesRequest { OrderedIds = orderedIds })).IsSuccessStatusCode;

    /// <summary>Exports the visible rules as portable definitions (category/account by name).</summary>
    public async Task<IReadOnlyList<RuleExportDto>> ExportRulesAsync() =>
        await http.GetFromJsonAsync<List<RuleExportDto>>("api/rules/export") ?? [];

    public async Task<RuleImportResultDto?> ImportRulesAsync(IReadOnlyList<RuleExportDto> items)
    {
        var response = await http.PostAsJsonAsync("api/rules/import", items);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<RuleImportResultDto>() : null;
    }

    public async Task<bool> DeleteRuleAsync(Guid id) =>
        (await http.DeleteAsync($"api/rules/{id}")).IsSuccessStatusCode;

    /// <summary>Soft-deleted rules, for the "deleted rules" panel.</summary>
    public async Task<IReadOnlyList<CategorizationRuleDto>> GetDeletedRulesAsync() =>
        await http.GetFromJsonAsync<List<CategorizationRuleDto>>("api/rules/deleted") ?? [];

    public async Task<bool> RestoreRuleAsync(Guid id) =>
        (await http.PostAsync($"api/rules/{id}/restore", null)).IsSuccessStatusCode;

    /// <summary>Re-runs the family's rules over existing transactions; returns how many were categorized.</summary>
    public async Task<int> RecategorizeAsync(bool onlyUncategorized)
    {
        var response = await http.PostAsJsonAsync("api/rules/recategorize",
            new RecategorizeRequest { OnlyUncategorized = onlyUncategorized });
        if (!response.IsSuccessStatusCode)
            return 0;
        var result = await response.Content.ReadFromJsonAsync<BulkResultDto>();
        return result?.Count ?? 0;
    }

    // --- Family members & invitations ---

    public async Task<IReadOnlyList<FamilyMemberDto>> GetMembersAsync() =>
        await http.GetFromJsonAsync<List<FamilyMemberDto>>("api/members") ?? [];

    public async Task<CreateMemberResultDto?> CreateMemberAsync(CreateMemberRequest request)
    {
        var response = await http.PostAsJsonAsync("api/members", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<CreateMemberResultDto>()
            : null;
    }

    public async Task<SaveStatus> UpdateMemberAsync(Guid memberId, UpdateMemberRequest request) =>
        ToStatus(await http.PutAsJsonAsync($"api/members/{memberId}", request));

    public async Task<bool> DeleteMemberAsync(Guid memberId) =>
        (await http.DeleteAsync($"api/members/{memberId}")).IsSuccessStatusCode;

    public async Task<bool> SetMemberActiveAsync(Guid memberId, bool active) =>
        (await http.PostAsync($"api/members/{memberId}/{(active ? "activate" : "deactivate")}", null)).IsSuccessStatusCode;

    public async Task<InvitationDto?> ReinviteMemberAsync(Guid memberId)
    {
        var response = await http.PostAsync($"api/members/{memberId}/invite", null);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<InvitationDto>()
            : null;
    }

    public async Task<IReadOnlyList<InvitationDto>> GetInvitationsAsync()
    {
        var response = await http.GetAsync("api/invitations");
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<List<InvitationDto>>() ?? []
            : [];
    }

    public async Task<bool> RevokeInvitationAsync(Guid invitationId) =>
        (await http.PostAsync($"api/invitations/{invitationId}/revoke", null)).IsSuccessStatusCode;

    // --- Per-account access (disponents) ---

    public Task<AccountAccessOverviewDto?> GetAccountAccessAsync(Guid accountId) =>
        http.GetFromJsonAsync<AccountAccessOverviewDto>($"api/accounts/{accountId}/access");

    public async Task<AccountAccessDto?> GrantAccessAsync(Guid accountId, GrantAccessRequest request)
    {
        var response = await http.PostAsJsonAsync($"api/accounts/{accountId}/access", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AccountAccessDto>()
            : null;
    }

    public async Task<bool> RevokeAccessAsync(Guid accountId, Guid memberId) =>
        (await http.DeleteAsync($"api/accounts/{accountId}/access/{memberId}")).IsSuccessStatusCode;

    // --- Cards ---

    public async Task<IReadOnlyList<BankCardDto>> GetCardsAsync(Guid accountId) =>
        await http.GetFromJsonAsync<List<BankCardDto>>($"api/accounts/{accountId}/cards") ?? [];

    public async Task<BankCardDto?> CreateCardAsync(Guid accountId, CreateCardRequest request)
    {
        var response = await http.PostAsJsonAsync($"api/accounts/{accountId}/cards", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<BankCardDto>()
            : null;
    }

    public async Task<SaveStatus> UpdateCardAsync(Guid cardId, UpdateCardRequest request) =>
        ToStatus(await http.PutAsJsonAsync($"api/cards/{cardId}", request));

    public async Task<bool> DeleteCardAsync(Guid cardId) =>
        (await http.DeleteAsync($"api/cards/{cardId}")).IsSuccessStatusCode;

    // --- Roles & permissions (per family) ---

    public Task<FamilyRolesDto?> GetRolesAsync() =>
        http.GetFromJsonAsync<FamilyRolesDto>("api/roles");

    public async Task<bool> UpdateRoleAsync(MemberRole role, IReadOnlyList<Permission> permissions) =>
        (await http.PutAsJsonAsync($"api/roles/{role}", new UpdateRolePermissionsRequest { Permissions = permissions })).IsSuccessStatusCode;

    // --- Family management ---

    public Task<FamilyDto?> GetFamilyAsync() =>
        http.GetFromJsonAsync<FamilyDto>("api/family");

    public async Task<SaveStatus> UpdateFamilyAsync(UpdateFamilyRequest request) =>
        ToStatus(await http.PutAsJsonAsync("api/family", request));

    private static SaveStatus ToStatus(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? SaveStatus.Success
        : response.StatusCode == HttpStatusCode.Conflict ? SaveStatus.Conflict
        : SaveStatus.Failed;

    public async Task<bool> TransferOwnershipAsync(Guid newOwnerMemberId) =>
        (await http.PostAsJsonAsync("api/family/transfer-ownership", new TransferOwnershipRequest { NewOwnerMemberId = newOwnerMemberId })).IsSuccessStatusCode;

    public async Task<bool> DeleteFamilyAsync(string confirmName)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "api/family")
        {
            Content = JsonContent.Create(new DeleteFamilyRequest { ConfirmName = confirmName }),
        };
        return (await http.SendAsync(request)).IsSuccessStatusCode;
    }

    // --- System administration (admin only) ---

    public async Task<AdminUserPageDto?> GetAdminUsersAsync(int page = 1, int pageSize = 25, string? search = null, string? status = null)
    {
        var url = $"api/admin/users?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
            url += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(status))
            url += $"&status={Uri.EscapeDataString(status)}";
        var response = await http.GetAsync(url);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AdminUserPageDto>()
            : null;
    }

    public async Task<bool> CreateUserAsync(CreateUserRequest request) =>
        (await http.PostAsJsonAsync("api/admin/users", request)).IsSuccessStatusCode;

    public async Task<bool> SetUserRolesAsync(Guid userId, IReadOnlyList<string> roleNames) =>
        (await http.PutAsJsonAsync($"api/admin/users/{userId}/roles", new SetUserRolesRequest { RoleNames = roleNames })).IsSuccessStatusCode;

    // --- System roles & permissions ---

    public Task<SystemRolesDto?> GetSystemRolesAsync() =>
        http.GetFromJsonAsync<SystemRolesDto>("api/admin/system-roles");

    public async Task<bool> CreateSystemRoleAsync(string name) =>
        (await http.PostAsJsonAsync("api/admin/system-roles", new CreateSystemRoleRequest { Name = name })).IsSuccessStatusCode;

    public async Task<bool> RenameSystemRoleAsync(Guid roleId, string name) =>
        (await http.PutAsJsonAsync($"api/admin/system-roles/{roleId}", new RenameSystemRoleRequest { Name = name })).IsSuccessStatusCode;

    public async Task<bool> SetSystemRolePermissionsAsync(Guid roleId, IReadOnlyList<SystemPermission> permissions) =>
        (await http.PutAsJsonAsync($"api/admin/system-roles/{roleId}/permissions", new UpdateSystemRolePermissionsRequest { Permissions = permissions })).IsSuccessStatusCode;

    public async Task<bool> DeleteSystemRoleAsync(Guid roleId) =>
        (await http.DeleteAsync($"api/admin/system-roles/{roleId}")).IsSuccessStatusCode;

    // --- Audit log ---

    public async Task<AuditPageDto?> GetAuditAsync(
        string? search = null, string? action = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null,
        int page = 1)
    {
        var response = await http.GetAsync(BuildAuditUrl("api/admin/audit", search, action, from, to, page));
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AuditPageDto>()
            : null;
    }

    public async Task<IReadOnlyList<string>> GetAuditActionsAsync()
    {
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

    public async Task<bool> LockUserAsync(Guid userId, int minutes) =>
        (await http.PostAsJsonAsync($"api/admin/users/{userId}/lock", new LockUserRequest { Minutes = minutes })).IsSuccessStatusCode;

    public async Task<bool> BlockUserAsync(Guid userId) =>
        (await http.PostAsync($"api/admin/users/{userId}/block", null)).IsSuccessStatusCode;

    public async Task<bool> RestoreUserAsync(Guid userId) =>
        (await http.PostAsync($"api/admin/users/{userId}/restore", null)).IsSuccessStatusCode;

    public async Task<bool> SetUserPasswordAsync(Guid userId, AdminSetPasswordRequest request) =>
        (await http.PostAsJsonAsync($"api/admin/users/{userId}/password", request)).IsSuccessStatusCode;

    public async Task<bool> ForcePasswordChangeAsync(Guid userId) =>
        (await http.PostAsync($"api/admin/users/{userId}/force-password-change", null)).IsSuccessStatusCode;

    public async Task<bool> DisableUser2faAsync(Guid userId) =>
        (await http.PostAsync($"api/admin/users/{userId}/disable-2fa", null)).IsSuccessStatusCode;

    public async Task<bool> Require2faAsync(Guid userId, DateTimeOffset? deadlineUtc) =>
        (await http.PostAsJsonAsync($"api/admin/users/{userId}/require-2fa-by",
            new Require2faRequest { DeadlineUtc = deadlineUtc })).IsSuccessStatusCode;

    public async Task<bool> ForceLogoutAsync(Guid userId) =>
        (await http.PostAsync($"api/admin/users/{userId}/force-logout", null)).IsSuccessStatusCode;

    public async Task<bool> DeleteUserAsync(Guid userId) =>
        (await http.DeleteAsync($"api/admin/users/{userId}")).IsSuccessStatusCode;

    public async Task<AdminUserPageDto?> GetDeletedUsersAsync(int page = 1, int pageSize = 25, string? search = null)
    {
        var url = $"api/admin/users/deleted?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
            url += $"&search={Uri.EscapeDataString(search)}";
        var response = await http.GetAsync(url);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AdminUserPageDto>()
            : null;
    }

    public async Task<bool> UndeleteUserAsync(Guid userId) =>
        (await http.PostAsync($"api/admin/users/{userId}/undelete", null)).IsSuccessStatusCode;

    public async Task<bool> PurgeUserAsync(Guid userId) =>
        (await http.DeleteAsync($"api/admin/users/{userId}/purge")).IsSuccessStatusCode;
}
