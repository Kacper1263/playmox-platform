using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace PlaymoxWeb.Services;

public class PlaymoxApiClient
{
    private readonly HttpClient httpClient;
    private readonly ISnackbar snackbar;
    private readonly AuthStateService authStateService;
    private readonly NavigationManager navigationManager;
    private readonly LocalizationService localization;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PlaymoxApiClient(
        HttpClient httpClient,
        ISnackbar snackbar,
        AuthStateService authStateService,
        NavigationManager navigationManager,
        LocalizationService localization)
    {
        this.httpClient = httpClient;
        this.snackbar = snackbar;
        this.authStateService = authStateService;
        this.navigationManager = navigationManager;
        this.localization = localization;
    }

    public async Task<T?> GetJsonAsync<T>(string url)
    {
        var response = await ExecuteWithAuthRetry(() => httpClient.GetAsync(url));
        if (!response.IsSuccessStatusCode)
        {
            await ShowApiError(response);
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(jsonOptions);
    }

    public Task<(bool Success, TResponse? Data)> PostAsync<TResponse>(string url, object payload)
        => PostJsonAsync<object, TResponse>(url, payload);

    public async Task<(bool Success, TResponse? Data)> PostJsonAsync<TRequest, TResponse>(string url, TRequest payload)
    {
        var response = await ExecuteWithAuthRetry(() => httpClient.PostAsJsonAsync(url, payload));
        if (!response.IsSuccessStatusCode)
        {
            await ShowApiError(response);
            return (false, default);
        }

        var data = await response.Content.ReadFromJsonAsync<TResponse>(jsonOptions);
        return (true, data);
    }

    public async Task<bool> PostJsonAsync<TRequest>(string url, TRequest payload)
    {
        var response = await ExecuteWithAuthRetry(() => httpClient.PostAsJsonAsync(url, payload));
        if (!response.IsSuccessStatusCode)
        {
            await ShowApiError(response);
            return false;
        }

        return true;
    }

    public async Task<(bool Success, TResponse? Data)> PutJsonAsync<TRequest, TResponse>(string url, TRequest payload)
    {
        var response = await ExecuteWithAuthRetry(() => httpClient.PutAsJsonAsync(url, payload));
        if (!response.IsSuccessStatusCode)
        {
            await ShowApiError(response);
            return (false, default);
        }

        var data = await response.Content.ReadFromJsonAsync<TResponse>(jsonOptions);
        return (true, data);
    }

    public async Task<bool> DeleteAsync(string url)
    {
        var response = await ExecuteWithAuthRetry(() => httpClient.DeleteAsync(url));
        if (!response.IsSuccessStatusCode)
        {
            await ShowApiError(response);
            return false;
        }

        return true;
    }

    public async Task<(bool Success, DownloadedFile? File)> DownloadFileAsync(string url)
    {
        HttpResponseMessage response;
        try
        {
            response = await ExecuteWithAuthRetry(() => httpClient.GetAsync(url));
        }
        catch (TaskCanceledException)
        {
            snackbar.Add(localization.T(Tr.Common.FileDownloadTimedOut), Severity.Error);
            return (false, null);
        }
        catch (HttpRequestException ex)
        {
            snackbar.Add(localization.T(Tr.Common.FileDownloadFailed, ex.Message), Severity.Error);
            return (false, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            await ShowApiError(response);
            return (false, null);
        }

        var content = await response.Content.ReadAsByteArrayAsync();
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? "backup.tar.gz";
        fileName = fileName.Trim('"');
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return (true, new DownloadedFile
        {
            FileName = fileName,
            ContentType = contentType,
            Content = content
        });
    }

    public async Task<bool> PostMultipartAsync(string url, MultipartFormDataContent payload)
    {
        HttpResponseMessage response;
        try
        {
            response = await ExecuteWithAuthRetry(() => httpClient.PostAsync(url, payload));
        }
        catch (TaskCanceledException)
        {
            snackbar.Add(localization.T(Tr.Common.FileUploadTimedOut), Severity.Error);
            return false;
        }
        catch (HttpRequestException ex)
        {
            snackbar.Add(localization.T(Tr.Common.FileUploadFailed, ex.Message), Severity.Error);
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            await ShowApiError(response);
            return false;
        }

        return true;
    }

    private async Task<HttpResponseMessage> ExecuteWithAuthRetry(Func<Task<HttpResponseMessage>> requestFactory)
    {
        var response = await requestFactory();
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        var refreshed = await authStateService.TryRefreshTokenAsync();
        if (!refreshed)
        {
            HandleSessionExpired();
            return new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);
        }

        var retryResponse = await requestFactory();
        if (retryResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            retryResponse.Dispose();
            await authStateService.LogoutAsync();
            HandleSessionExpired();
            return new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);
        }

        return retryResponse;
    }

    private void HandleSessionExpired()
    {
        snackbar.Add(localization.T(Tr.Common.SessionExpired), Severity.Warning);

        var currentUri = new Uri(navigationManager.Uri);
        if (!currentUri.AbsolutePath.Equals("/login", StringComparison.OrdinalIgnoreCase))
        {
            navigationManager.NavigateTo("/login");
        }
    }

    private async Task ShowApiError(HttpResponseMessage response)
    {
        var errorMessage = await TryGetApiMessage(response);
        var baseMessage = localization.T(Tr.Common.ApiErrorStatus, (int)response.StatusCode);

        var fullMessage = string.IsNullOrWhiteSpace(errorMessage) ? baseMessage : $"{baseMessage}: {errorMessage}";
        snackbar.Add(fullMessage, Severity.Error, config => { config.RequireInteraction = true; });
    }

    private async Task<string?> TryGetApiMessage(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
            return response.ReasonPhrase;
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

                if (json.RootElement.TryGetProperty("detail", out var detailProp))
                {
                    return detailProp.GetString();
                }

                if (json.RootElement.TryGetProperty("title", out var titleProp))
                {
                    return titleProp.GetString();
                }
            }
        }
        catch
        {
            // ignore parsing issues
        }

        if (content.TrimStart().StartsWith("{"))
        {
            return response.ReasonPhrase;
        }

        return content.Length > 400 ? content[..400] : content;
    }
}

public class DownloadedFile
{
    public string FileName { get; set; } = "download.bin";
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = [];
}
