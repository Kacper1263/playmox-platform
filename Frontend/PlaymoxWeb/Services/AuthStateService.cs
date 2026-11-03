using System.Text.Json;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.JSInterop;
using PlaymoxWeb.Models;

namespace PlaymoxWeb.Services;

public class AuthStateService
{
    private const string AccessTokenStorageKey = "playmox.access-token";
    private const string RefreshTokenStorageKey = "playmox.refresh-token";
    private const string UserStorageKey = "playmox.user";

    private readonly HttpClient apiHttpClient;
    private readonly IJSRuntime jsRuntime;
    private readonly LocalizationService localization;
    private readonly SemaphoreSlim initializeSemaphore = new(1, 1);
    private readonly SemaphoreSlim refreshSemaphore = new(1, 1);

    private string? currentAccessToken;
    private string? currentRefreshToken;

    public bool IsInitialized { get; private set; }
    public bool IsInitializing { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public UserDto? CurrentUser { get; private set; }
    public string? InitializationError { get; private set; }

    public event Action? StateChanged;

    public AuthStateService(HttpClient apiHttpClient, IJSRuntime jsRuntime, LocalizationService localization)
    {
        this.apiHttpClient = apiHttpClient;
        this.jsRuntime = jsRuntime;
        this.localization = localization;
    }

    public async Task InitializeAsync()
    {
        if (IsInitialized)
        {
            return;
        }

        await initializeSemaphore.WaitAsync();
        try
        {
            if (IsInitialized)
            {
                return;
            }

            IsInitializing = true;
            NotifyStateChanged();

            var token = await jsRuntime.InvokeAsync<string?>("playmoxAuth.get", AccessTokenStorageKey);
            var refreshToken = await jsRuntime.InvokeAsync<string?>("playmoxAuth.get", RefreshTokenStorageKey);
            var userJson = await jsRuntime.InvokeAsync<string?>("playmoxAuth.get", UserStorageKey);

            if (!string.IsNullOrWhiteSpace(token))
            {
                currentAccessToken = token;
                currentRefreshToken = refreshToken;
                apiHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                IsAuthenticated = true;
            }

            if (!string.IsNullOrWhiteSpace(userJson))
            {
                CurrentUser = JsonSerializer.Deserialize<UserDto>(userJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }
        catch (Exception ex)
        {
            InitializationError = localization.T(Tr.Common.SessionReadFailed, ex.Message);
        }
        finally
        {
            IsInitializing = false;
            IsInitialized = true;
            NotifyStateChanged();
            initializeSemaphore.Release();
        }
    }

    public async Task<(bool Success, string? Error)> LoginAsync(LoginRequest request)
    {
        var response = await apiHttpClient.PostAsJsonAsync("api/auth/login", request);
        if (!response.IsSuccessStatusCode)
        {
            var message = await TryGetApiMessage(response);
            var fallback = localization.T(Tr.Login.LoginFailed);
            return (false, string.IsNullOrWhiteSpace(message) ? fallback : message);
        }

        var tokenModel = await response.Content.ReadFromJsonAsync<TokenApiModel>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (tokenModel == null || string.IsNullOrWhiteSpace(tokenModel.AccessToken))
        {
            return (false, localization.T(Tr.Login.MissingTokenInResponse));
        }

        await SetSessionAsync(tokenModel);
        NotifyStateChanged();
        return (true, null);
    }

    public async Task<bool> TryRefreshTokenAsync()
    {
        await InitializeAsync();
        if (!IsAuthenticated || string.IsNullOrWhiteSpace(currentAccessToken) || string.IsNullOrWhiteSpace(currentRefreshToken))
        {
            await LogoutInternalAsync();
            NotifyStateChanged();
            return false;
        }

        await refreshSemaphore.WaitAsync();
        try
        {
            if (!IsAuthenticated || string.IsNullOrWhiteSpace(currentAccessToken) || string.IsNullOrWhiteSpace(currentRefreshToken))
            {
                return false;
            }

            var response = await apiHttpClient.PostAsJsonAsync("api/auth/refresh", new TokenApiModel
            {
                AccessToken = currentAccessToken,
                RefreshToken = currentRefreshToken
            });

            if (!response.IsSuccessStatusCode)
            {
                await LogoutInternalAsync();
                NotifyStateChanged();
                return false;
            }

            var refreshedTokens = await response.Content.ReadFromJsonAsync<TokenApiModel>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (refreshedTokens == null || string.IsNullOrWhiteSpace(refreshedTokens.AccessToken))
            {
                await LogoutInternalAsync();
                NotifyStateChanged();
                return false;
            }

            await SetSessionAsync(refreshedTokens);
            NotifyStateChanged();
            return true;
        }
        finally
        {
            refreshSemaphore.Release();
        }
    }

    public async Task LogoutAsync()
    {
        if (!string.IsNullOrWhiteSpace(currentAccessToken))
        {
            try
            {
                await apiHttpClient.PostAsJsonAsync("api/auth/revoke", new { refreshToken = currentRefreshToken });
            }
            catch (HttpRequestException)
            {
                // ignore revoke network failure on logout
            }
        }

        await LogoutInternalAsync();
        NotifyStateChanged();
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        await InitializeAsync();
        if (!string.IsNullOrWhiteSpace(currentAccessToken))
        {
            return currentAccessToken;
        }

        var token = await jsRuntime.InvokeAsync<string?>("playmoxAuth.get", AccessTokenStorageKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            currentAccessToken = token;
        }

        return currentAccessToken;
    }

    public async Task<string?> GetValidAccessTokenAsync(TimeSpan? minimumLifetime = null)
    {
        var token = await GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var minLifetime = minimumLifetime ?? TimeSpan.FromMinutes(1);
        if (!IsJwtExpiringSoon(token, minLifetime))
        {
            return token;
        }

        var refreshed = await TryRefreshTokenAsync();
        if (!refreshed || string.IsNullOrWhiteSpace(currentAccessToken))
        {
            return null;
        }

        return currentAccessToken;
    }

    private async Task SetSessionAsync(TokenApiModel tokenModel)
    {
        CurrentUser = tokenModel.UserBO;
        IsAuthenticated = true;
        currentAccessToken = tokenModel.AccessToken;
        currentRefreshToken = tokenModel.RefreshToken;
        apiHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenModel.AccessToken);

        await jsRuntime.InvokeVoidAsync("playmoxAuth.set", AccessTokenStorageKey, tokenModel.AccessToken);
        await jsRuntime.InvokeVoidAsync("playmoxAuth.set", RefreshTokenStorageKey, tokenModel.RefreshToken ?? string.Empty);
        await jsRuntime.InvokeVoidAsync("playmoxAuth.set", UserStorageKey, JsonSerializer.Serialize(CurrentUser));
    }

    private async Task LogoutInternalAsync()
    {
        IsAuthenticated = false;
        CurrentUser = null;
        currentAccessToken = null;
        currentRefreshToken = null;
        apiHttpClient.DefaultRequestHeaders.Authorization = null;

        await jsRuntime.InvokeVoidAsync("playmoxAuth.remove", AccessTokenStorageKey);
        await jsRuntime.InvokeVoidAsync("playmoxAuth.remove", RefreshTokenStorageKey);
        await jsRuntime.InvokeVoidAsync("playmoxAuth.remove", UserStorageKey);
    }

    public bool IsAdmin()
    {
        if (CurrentUser?.UserRoles == null)
        {
            return false;
        }

        return CurrentUser.UserRoles.Any(r =>
            string.Equals(r.Role?.Name, "Admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.Role?.Name, "Administrator", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string?> TryGetApiMessage(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(content);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (json.RootElement.TryGetProperty("message", out var messageProp))
                {
                    return messageProp.GetString();
                }

                if (json.RootElement.TryGetProperty("Message", out var messagePropUpper))
                {
                    return messagePropUpper.GetString();
                }

                if (json.RootElement.TryGetProperty("value", out var valueProp))
                {
                    return valueProp.GetString();
                }

                if (json.RootElement.TryGetProperty("Value", out var valuePropUpper))
                {
                    return valuePropUpper.GetString();
                }
            }
        }
        catch
        {
            // ignore parsing issues
        }

        return content.Length > 400 ? content[..400] : content;
    }

    private static bool IsJwtExpiringSoon(string token, TimeSpan minimumLifetime)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
            {
                return true;
            }

            var payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');

            var padding = payload.Length % 4;
            if (padding != 0)
            {
                payload = payload.PadRight(payload.Length + (4 - padding), '=');
            }

            var payloadBytes = Convert.FromBase64String(payload);
            using var json = JsonDocument.Parse(payloadBytes);
            if (!json.RootElement.TryGetProperty("exp", out var expProperty))
            {
                return true;
            }

            long expSeconds;
            if (expProperty.ValueKind == JsonValueKind.Number)
            {
                if (!expProperty.TryGetInt64(out expSeconds))
                {
                    return true;
                }
            }
            else if (expProperty.ValueKind == JsonValueKind.String)
            {
                if (!long.TryParse(expProperty.GetString(), out expSeconds))
                {
                    return true;
                }
            }
            else
            {
                return true;
            }

            var expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
            return expiresAtUtc <= DateTimeOffset.UtcNow.Add(minimumLifetime);
        }
        catch
        {
            return true;
        }
    }

    private void NotifyStateChanged() => StateChanged?.Invoke();
}
