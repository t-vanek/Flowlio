using System.Net.Http.Json;
using Flowlio.Client.Auth;
using Flowlio.Domain;
using Flowlio.Shared;

namespace Flowlio.Client.Services;

/// <summary>Typed wrapper over the Flowlio HTTP API used by the Blazor components.</summary>
public sealed class FlowlioApi(HttpClient http, TokenProvider tokens, JwtAuthStateProvider authState)
{
    public async Task<bool> LoginAsync(string username, string password)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "profile email offline_access flowlio.api",
        });

        var response = await http.PostAsync("connect/token", form);
        if (!response.IsSuccessStatusCode)
            return false;

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (payload?.AccessToken is null)
            return false;

        await tokens.SetTokensAsync(payload.AccessToken, payload.RefreshToken);
        authState.NotifyChanged();
        return true;
    }

    public async Task LogoutAsync()
    {
        await tokens.ClearAsync();
        authState.NotifyChanged();
    }

    public async Task<(bool Success, string? Error)> RegisterAsync(RegisterRequest request)
    {
        var response = await http.PostAsJsonAsync("api/auth/register", request);
        if (response.IsSuccessStatusCode)
            return (true, null);

        var problem = await response.Content.ReadAsStringAsync();
        return (false, problem);
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

    private sealed record TokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }
    }
}
