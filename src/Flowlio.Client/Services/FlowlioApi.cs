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

    public async Task<bool> DeleteMemberAsync(Guid memberId) =>
        (await http.DeleteAsync($"api/members/{memberId}")).IsSuccessStatusCode;

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
}
