using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace PlaymoxWeb.Services;

public record LanguageOption(string Code, string Name);

public class LocalizationService
{
    private const string LanguageStorageKey = "playmox_lang";
    private readonly IJSRuntime _jsRuntime;
    private readonly HttpClient _httpClient;
    private readonly NavigationManager _navigationManager;

    private readonly Dictionary<string, Dictionary<string, string>> _loadedDictionaries = new(StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlyList<LanguageOption> SupportedLanguages = new[]
    {
        new LanguageOption("en", "English"),
        new LanguageOption("pl", "Polski")
    };

    public string CurrentLanguage { get; private set; } = "en";

    public bool IsPolish => string.Equals(CurrentLanguage, "pl", StringComparison.OrdinalIgnoreCase);

    public LanguageOption CurrentLanguageInfo
        => SupportedLanguages.FirstOrDefault(x => string.Equals(x.Code, CurrentLanguage, StringComparison.OrdinalIgnoreCase))
           ?? SupportedLanguages[0];

    public event Action? OnLanguageChanged;

    public LocalizationService(IJSRuntime jsRuntime, HttpClient httpClient, NavigationManager navigationManager)
    {
        _jsRuntime = jsRuntime;
        _httpClient = httpClient;
        _navigationManager = navigationManager;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var stored = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", LanguageStorageKey);
            if (string.IsNullOrWhiteSpace(stored))
            {
                var cookieStr = await _jsRuntime.InvokeAsync<string?>("eval", "document.cookie");
                if (!string.IsNullOrWhiteSpace(cookieStr))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(cookieStr, @"(?:^|;\s*)playmox_lang=([^;]+)");
                    if (match.Success)
                    {
                        stored = match.Groups[1].Value.Trim();
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(stored) &&
                SupportedLanguages.Any(l => string.Equals(l.Code, stored, StringComparison.OrdinalIgnoreCase)))
            {
                CurrentLanguage = stored.ToLowerInvariant();
            }
            else
            {
                CurrentLanguage = "en";
            }
        }
        catch
        {
            CurrentLanguage = "en";
        }

        await LoadLanguageDictionaryAsync("en");
        if (!string.Equals(CurrentLanguage, "en", StringComparison.OrdinalIgnoreCase))
        {
            await LoadLanguageDictionaryAsync(CurrentLanguage);
        }

        OnLanguageChanged?.Invoke();
    }

    public async Task SetLanguageAsync(string lang)
    {
        var target = SupportedLanguages.FirstOrDefault(l => string.Equals(l.Code, lang, StringComparison.OrdinalIgnoreCase))?.Code ?? "en";
        if (string.Equals(CurrentLanguage, target, StringComparison.OrdinalIgnoreCase) && _loadedDictionaries.ContainsKey(target))
        {
            return;
        }

        CurrentLanguage = target;
        await LoadLanguageDictionaryAsync(CurrentLanguage);

        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", LanguageStorageKey, CurrentLanguage);
            await _jsRuntime.InvokeVoidAsync("eval", $"document.cookie = 'playmox_lang={CurrentLanguage}; path=/; max-age=31536000; SameSite=Lax'");
        }
        catch
        {
            // ignore storage errors
        }

        OnLanguageChanged?.Invoke();
    }

    public Task ToggleLanguageAsync()
        => SetLanguageAsync(IsPolish ? "en" : "pl");

    private async Task LoadLanguageDictionaryAsync(string langCode)
    {
        if (_loadedDictionaries.ContainsKey(langCode))
        {
            return;
        }

        try
        {
            var baseUrl = _navigationManager?.BaseUri ?? _httpClient.BaseAddress?.ToString() ?? "/";
            var url = $"{baseUrl.TrimEnd('/')}/locales/{langCode}.json";
            var dict = await _httpClient.GetFromJsonAsync<Dictionary<string, string>>(url);
            if (dict != null)
            {
                _loadedDictionaries[langCode] = new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // fallback if JSON not accessible
        }
    }

    /// <summary>
    /// Translates a strongly-typed key from <see cref="Tr"/> using the active language JSON dictionary.
    /// Falls back to the English dictionary, then to the key string.
    /// </summary>
    public string T(string key, params object?[] args)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        string? value = null;
        if (_loadedDictionaries.TryGetValue(CurrentLanguage, out var dict))
        {
            dict.TryGetValue(key, out value);
        }

        if (string.IsNullOrEmpty(value) && _loadedDictionaries.TryGetValue("en", out var enDict))
        {
            enDict.TryGetValue(key, out value);
        }

        value ??= key;

        if (args != null && args.Length > 0)
        {
            try
            {
                return string.Format(value, args);
            }
            catch
            {
                return value;
            }
        }

        return value;
    }

    public string this[string key] => T(key);

    public string TranslateStatusLabel(string? statusCode, string? fallbackLabel = null)
    {
        var key = (statusCode ?? fallbackLabel ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "creating" => T(Tr.Status.Creating),
            "running" => T(Tr.Status.Running),
            "stopped" => T(Tr.Status.Stopped),
            "updating" => T(Tr.Status.Updating),
            "deleting" => T(Tr.Status.Deleting),
            "deleted" => T(Tr.Status.Deleted),
            "error" => T(Tr.Common.Error),
            "queued" => T(Tr.Status.Queued),
            "succeeded" => T(Tr.Status.Succeeded),
            "failed" => T(Tr.Status.Failed),
            "cancelled" or "canceled" => T(Tr.Status.Cancelled),
            _ => !string.IsNullOrWhiteSpace(fallbackLabel) ? fallbackLabel : (statusCode ?? string.Empty)
        };
    }

    public string TranslateOperationLabel(string? operationCode, string? fallbackLabel = null)
    {
        var key = (operationCode ?? fallbackLabel ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "create" or "createvm" or "create_vm" or "create vm" => T(Tr.Instances.CreateVm),
            "start" or "startvm" or "start_vm" or "start vm" => T(Tr.Operation.StartVm),
            "stop" or "stopvm" or "stop_vm" or "force-stop" or "forcestop" or "stop vm" => T(Tr.Operation.StopVm),
            "shutdown" or "shutdownvm" or "shutdown_vm" or "shutdown vm" => T(Tr.Operation.ShutdownVm),
            "restart" or "restartvm" or "restart_vm" or "restart vm" => T(Tr.Operation.RestartVm),
            "delete" or "deletevm" or "delete_vm" or "delete vm" => T(Tr.Instances.MenuDeleteVm),
            "updateresources" or "update_resources" or "updatevmresources" or "resize" or "update vm resources" => T(Tr.Operation.UpdateResources),
            "updateconfig" or "update_config" or "updategameconfig" or "updategameconfiguration" or "update_game_config" or "update game configuration" => T(Tr.Operation.UpdateGameConfig),
            "install" or "installgameserver" or "reinstall" or "reinstallgame" or "reinstallgameserver" or "reinstall_game" or "reinstall game server" => T(Tr.Instances.MenuReinstallGame),
            "resetpassword" or "reset_password" or "resetvmpassword" or "resetsshpassword" or "reset vm password" => T(Tr.Instances.MenuResetSshPassword),
            "restore" or "restorebackup" or "restore_backup" => T(Tr.Instances.MenuRestoreBackup),
            _ => !string.IsNullOrWhiteSpace(fallbackLabel) ? fallbackLabel : (operationCode ?? string.Empty)
        };
    }

    public string TranslateGameName(string? gameKey, string? fallbackName = null)
    {
        var normalizedKey = (gameKey ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedName = (fallbackName ?? string.Empty).Trim();

        if (normalizedKey == "blank-vm" ||
            string.Equals(normalizedName, "Blank VM (No Game)", StringComparison.OrdinalIgnoreCase))
        {
            return T(Tr.Game.BlankVm);
        }

        return !string.IsNullOrWhiteSpace(fallbackName) ? fallbackName : (gameKey ?? string.Empty);
    }

    public string TranslateSchemaFieldTitle(string? fieldKey, string? fallbackTitle = null)
    {
        if (string.IsNullOrWhiteSpace(fieldKey))
        {
            return fallbackTitle ?? string.Empty;
        }

        var lookupKey = $"schema.{fieldKey.Trim()}.title";
        var translated = T(lookupKey);
        if (!string.Equals(translated, lookupKey, StringComparison.OrdinalIgnoreCase))
        {
            return translated;
        }

        return !string.IsNullOrWhiteSpace(fallbackTitle) ? fallbackTitle : fieldKey;
    }

    public string? TranslateSchemaFieldDescription(string? fieldKey, string? fallbackDescription = null)
    {
        if (string.IsNullOrWhiteSpace(fieldKey))
        {
            return fallbackDescription;
        }

        var lookupKey = $"schema.{fieldKey.Trim()}.desc";
        var translated = T(lookupKey);
        if (!string.Equals(translated, lookupKey, StringComparison.OrdinalIgnoreCase))
        {
            return translated;
        }

        return fallbackDescription;
    }

    public string TranslateRuntimeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        return message.Trim() switch
        {
            "Blank virtual machine (no game server installed)." => T(Tr.Runtime.BlankVmMessage),
            "Game server is running." => T(Tr.Runtime.ServerRunning),
            "Game server is stopped." => T(Tr.Runtime.ServerStopped),
            "Virtual machine is not running." => T(Tr.Runtime.VmNotRunning),
            _ => message
        };
    }
}
