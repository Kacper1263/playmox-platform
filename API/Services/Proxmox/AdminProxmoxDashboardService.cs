using System.Text.Json;
using PlaymoxAPI.BLL;
using PlaymoxAPI.BLL.BO.Platform;
using PlaymoxAPI.BLL.DTO.Platform;
using PlaymoxAPI.Helpers;
using Microsoft.EntityFrameworkCore;

namespace PlaymoxAPI.Services.Proxmox;

public class AdminProxmoxDashboardService
{
    public async Task<AdminProxmoxDashboardDto> GetDashboardAsync()
    {
        using var dbContext = new PlaymoxContext();
        var settings = await dbContext.ProxmoxSettings
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync();

        if (settings == null || string.IsNullOrWhiteSpace(settings.ApiTokenSecret))
        {
            return new AdminProxmoxDashboardDto
            {
                IsConfigured = false,
                IsConnected = false,
                Message = "Configure Proxmox connection in the Configuration tab first."
            };
        }

        try
        {
            var tokenSecret = SecureSecret.Decrypt(settings.ApiTokenSecret);
            if (string.IsNullOrWhiteSpace(tokenSecret))
            {
                return new AdminProxmoxDashboardDto
                {
                    IsConfigured = true,
                    IsConnected = false,
                    Message = "Missing or invalid API token secret in Proxmox configuration."
                };
            }

            var baseUrl = NormalizeBaseUrl(settings.ProxmoxUrl);

            using var handler = new HttpClientHandler();
            if (Config.AllowSelfSignedProxmoxCertificates)
            {
                handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            }
            using var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

            var tokenHeader = $"PVEAPIToken={settings.ApiUser}!{settings.ApiTokenId}={tokenSecret}";
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", tokenHeader);

            var versionJson = await GetJsonAsync(httpClient, $"{baseUrl}/version");
            var nodeStatusJson = await GetJsonAsync(httpClient, $"{baseUrl}/nodes/{settings.ProxmoxNode}/status");
            var qemuJson = await GetJsonAsync(httpClient, $"{baseUrl}/nodes/{settings.ProxmoxNode}/qemu");
            var lxcJson = await GetJsonAsync(httpClient, $"{baseUrl}/nodes/{settings.ProxmoxNode}/lxc");
            var storagesJson = await GetJsonAsync(httpClient, $"{baseUrl}/nodes/{settings.ProxmoxNode}/storage");
            var (vmCount, templateCount) = GetQemuCounts(qemuJson);

            var storages = ParseStorages(storagesJson);

            return new AdminProxmoxDashboardDto
            {
                IsConfigured = true,
                IsConnected = true,
                Message = "Proxmox connection is healthy.",
                ProxmoxUrl = settings.ProxmoxUrl,
                ProxmoxNode = settings.ProxmoxNode,
                ProxmoxVersion = GetString(versionJson, "data", "version") ?? string.Empty,
                CpuUsagePercent = ToPercent(GetDouble(nodeStatusJson, "data", "cpu")),
                CpuCores = (int?)GetInt64(nodeStatusJson, "data", "cpuinfo", "cpus"),
                MemoryUsedBytes = GetInt64(nodeStatusJson, "data", "memory", "used"),
                MemoryTotalBytes = GetInt64(nodeStatusJson, "data", "memory", "total"),
                RootFsUsedBytes = GetInt64(nodeStatusJson, "data", "rootfs", "used"),
                RootFsTotalBytes = GetInt64(nodeStatusJson, "data", "rootfs", "total"),
                VmCount = vmCount,
                TemplateCount = templateCount,
                CtCount = GetArrayLength(lxcJson, "data"),
                Storages = storages
            };
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            if (message.Contains("Permission check failed", StringComparison.OrdinalIgnoreCase))
            {
                message = $"{message} Check API token ACL and re-run the Proxmox bootstrap script to grant token permissions.";
            }

            return new AdminProxmoxDashboardDto
            {
                IsConfigured = true,
                IsConnected = false,
                Message = $"Failed to fetch data from Proxmox: {message}",
                ProxmoxUrl = settings.ProxmoxUrl,
                ProxmoxNode = settings.ProxmoxNode
            };
        }
    }

    private static string NormalizeBaseUrl(string proxmoxUrl)
    {
        var url = proxmoxUrl.TrimEnd('/');
        if (!url.EndsWith("/api2/json", StringComparison.OrdinalIgnoreCase))
        {
            url = $"{url}/api2/json";
        }

        return url;
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient httpClient, string url)
    {
        using var response = await httpClient.GetAsync(url);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode} for {url}: {raw}");
        }

        return JsonDocument.Parse(raw);
    }

    private static List<AdminProxmoxStorageDto> ParseStorages(JsonDocument storagesJson)
    {
        var result = new List<AdminProxmoxStorageDto>();
        if (!TryGetProperty(storagesJson.RootElement, "data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in data.EnumerateArray())
        {
            result.Add(new AdminProxmoxStorageDto
            {
                Name = GetString(item, "storage") ?? string.Empty,
                Type = GetString(item, "type") ?? string.Empty,
                Active = GetInt64(item, "active").GetValueOrDefault() == 1,
                UsedBytes = GetInt64(item, "used"),
                TotalBytes = GetInt64(item, "total")
            });
        }

        return result
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Type, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static (int VmCount, int TemplateCount) GetQemuCounts(JsonDocument qemuJson)
    {
        if (!TryGetPath(qemuJson.RootElement, new[] { "data" }, out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return (0, 0);
        }

        var templates = 0;
        var total = data.GetArrayLength();
        foreach (var item in data.EnumerateArray())
        {
            if (GetInt64(item, "template").GetValueOrDefault() == 1)
            {
                templates++;
            }
        }

        return (Math.Max(total - templates, 0), templates);
    }

    private static int GetArrayLength(JsonDocument json, params string[] path)
    {
        if (!TryGetPath(json.RootElement, path, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return element.GetArrayLength();
    }

    private static string? GetString(JsonDocument json, params string[] path)
        => TryGetPath(json.RootElement, path, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static string? GetString(JsonElement element, params string[] path)
        => TryGetPath(element, path, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? GetInt64(JsonDocument json, params string[] path)
        => TryGetPath(json.RootElement, path, out var element) ? AsInt64(element) : null;

    private static long? GetInt64(JsonElement element, params string[] path)
        => TryGetPath(element, path, out var value) ? AsInt64(value) : null;

    private static double? GetDouble(JsonDocument json, params string[] path)
        => TryGetPath(json.RootElement, path, out var element) ? AsDouble(element) : null;

    private static long? AsInt64(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt64(out var asLong) => asLong,
            JsonValueKind.Number when element.TryGetDouble(out var asDouble) => Convert.ToInt64(asDouble),
            JsonValueKind.String when long.TryParse(element.GetString(), out var fromString) => fromString,
            _ => null
        };
    }

    private static double? AsDouble(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDouble(out var value) => value,
            JsonValueKind.String when double.TryParse(element.GetString(), out var fromString) => fromString,
            _ => null
        };
    }

    private static double? ToPercent(double? ratio) => ratio.HasValue ? Math.Round(ratio.Value * 100, 2) : null;

    private static bool TryGetPath(JsonElement element, IEnumerable<string> path, out JsonElement value)
    {
        value = element;
        foreach (var node in path)
        {
            if (!TryGetProperty(value, node, out var next))
            {
                value = default;
                return false;
            }

            value = next;
        }

        return true;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}
