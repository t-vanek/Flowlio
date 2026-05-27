using System.Net.Http.Json;
using Flowlio.Domain;
using Flowlio.Shared;

namespace Flowlio.Client.Services;

/// <summary>Typed wrapper over the Flowlio HTTP API used by the Blazor components.</summary>
public sealed class FlowlioApi(HttpClient http)
{
    public Task<CurrentUserDto?> GetMeAsync() =>
        http.GetFromJsonAsync<CurrentUserDto>("api/me");

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

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync() =>
        await http.GetFromJsonAsync<List<CategoryDto>>("api/categories") ?? [];

    public async Task<TransactionPageDto?> GetTransactionsAsync(Guid? accountId = null, string? search = null, int page = 1)
    {
        var url = $"api/transactions?page={page}";
        if (accountId is { } id)
            url += $"&accountId={id}";
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

    public async Task<FamilyMemberDto?> UpdateMemberAsync(Guid memberId, UpdateMemberRequest request)
    {
        var response = await http.PutAsJsonAsync($"api/members/{memberId}", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<FamilyMemberDto>()
            : null;
    }

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

    public async Task<BankCardDto?> UpdateCardAsync(Guid cardId, UpdateCardRequest request)
    {
        var response = await http.PutAsJsonAsync($"api/cards/{cardId}", request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<BankCardDto>()
            : null;
    }

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

    public async Task<bool> UpdateFamilyAsync(UpdateFamilyRequest request) =>
        (await http.PutAsJsonAsync("api/family", request)).IsSuccessStatusCode;

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

    public async Task<IReadOnlyList<AdminUserDto>> GetAdminUsersAsync()
    {
        var response = await http.GetAsync("api/admin/users");
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<List<AdminUserDto>>() ?? []
            : [];
    }

    public async Task<bool> CreateUserAsync(CreateUserRequest request) =>
        (await http.PostAsJsonAsync("api/admin/users", request)).IsSuccessStatusCode;

    public async Task<bool> SetUserAdminAsync(Guid userId, bool isAdmin) =>
        (await http.PostAsJsonAsync($"api/admin/users/{userId}/admin", new SetUserAdminRequest { IsAdmin = isAdmin })).IsSuccessStatusCode;

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

    public async Task<bool> ForceLogoutAsync(Guid userId) =>
        (await http.PostAsync($"api/admin/users/{userId}/force-logout", null)).IsSuccessStatusCode;

    public async Task<bool> DeleteUserAsync(Guid userId) =>
        (await http.DeleteAsync($"api/admin/users/{userId}")).IsSuccessStatusCode;
}
