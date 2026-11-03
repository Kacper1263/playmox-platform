using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using PlaymoxWeb;
using PlaymoxWeb.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var configuredApiBaseUrl = builder.Configuration["ApiBaseUrl"];
Uri apiBaseUri;
if (string.IsNullOrWhiteSpace(configuredApiBaseUrl))
{
    apiBaseUri = new Uri(new Uri(builder.HostEnvironment.BaseAddress), "/");
}
else if (Uri.TryCreate(configuredApiBaseUrl, UriKind.Absolute, out var absoluteApiBaseUri))
{
    apiBaseUri = absoluteApiBaseUri;
}
else
{
    apiBaseUri = new Uri(new Uri(builder.HostEnvironment.BaseAddress), configuredApiBaseUrl);
}

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = apiBaseUri,
    Timeout = TimeSpan.FromMinutes(30)
});
builder.Services.AddMudServices();
builder.Services.AddScoped<LocalizationService>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<DynamicConfigService>();
builder.Services.AddScoped<PlaymoxApiClient>();
builder.Services.AddScoped<AuthStateService>();
builder.Services.AddScoped<TutorialService>();

await builder.Build().RunAsync();
