using Flowlio.Client;
using Flowlio.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using System.Globalization;

// Czech UI culture so dates (FluentDatePicker), numbers and the calendar render
// as dd.MM.yyyy with comma decimals. Wire-format DTOs stay invariant (System.Text.Json).
var czech = new CultureInfo("cs-CZ");
CultureInfo.DefaultThreadCurrentCulture = czech;
CultureInfo.DefaultThreadCurrentUICulture = czech;
CultureInfo.CurrentCulture = czech;
CultureInfo.CurrentUICulture = czech;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddFluentUIComponents();

// OpenID Connect (authorization-code + PKCE) against the OpenIddict server that also hosts this client.
builder.Services.AddOidcAuthentication(options =>
{
    options.ProviderOptions.Authority = builder.HostEnvironment.BaseAddress;
    options.ProviderOptions.ClientId = "flowlio-spa";
    options.ProviderOptions.ResponseType = "code";

    options.ProviderOptions.DefaultScopes.Add("openid");
    options.ProviderOptions.DefaultScopes.Add("profile");
    options.ProviderOptions.DefaultScopes.Add("email");
    options.ProviderOptions.DefaultScopes.Add("offline_access");
    options.ProviderOptions.DefaultScopes.Add("flowlio.api");

    options.UserOptions.NameClaim = "name";

    // After a successful logout, skip the default "You are logged out" screen and
    // send the user straight back to the login flow.
    options.AuthenticationPaths.LogOutSucceededPath = "authentication/login";
});

builder.Services
    .AddHttpClient("Flowlio", client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("Flowlio"));

builder.Services.AddScoped<FlowlioApi>();
builder.Services.AddScoped<CurrentUserState>();
builder.Services.AddScoped<LiveNotificationService>();

await builder.Build().RunAsync();
