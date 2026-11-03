using System.Text.Json;
using Microsoft.Extensions.Logging;
using PlaymoxAPI.BLL.BO.Platform;
using PlaymoxAPI.Helpers;
using PlaymoxAPI.Services.Proxmox;
using Renci.SshNet;

namespace PlaymoxAPI.Services.Platform;

internal static class GameServerConnectorSshOps
{
    public static SshClient CreateSshClient(string host, string username, string password)
    {
        var client = new SshClient(BuildConnectionInfo(host, username, password));
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
        client.KeepAliveInterval = TimeSpan.FromSeconds(15);
        return client;
    }

    public static SftpClient CreateSftpClient(string host, string username, string password)
    {
        var client = new SftpClient(BuildConnectionInfo(host, username, password));
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
        client.KeepAliveInterval = TimeSpan.FromSeconds(15);
        return client;
    }

    public static string NormalizeShellCommand(string command)
        => command.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    public static void UploadLocalFile(SftpClient sftp, string localFilePath, string remoteFilePath)
    {
        using var uploadStream = File.OpenRead(localFilePath);
        sftp.UploadFile(uploadStream, remoteFilePath, true);
    }

    public static Renci.SshNet.ConnectionInfo BuildConnectionInfo(string host, string username, string password)
    {
        var passwordMethod = new PasswordAuthenticationMethod(username, password);
        var keyboardMethod = new KeyboardInteractiveAuthenticationMethod(username);
        keyboardMethod.AuthenticationPrompt += (_, eventArgs) =>
        {
            foreach (var prompt in eventArgs.Prompts)
            {
                prompt.Response = password;
            }
        };

        return new Renci.SshNet.ConnectionInfo(host, 22, username, passwordMethod, keyboardMethod)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public static async Task<(string Host, string Username, string Password)> ExtractSshAccessAsync(
        VirtualGameInstance instance,
        ProxmoxAutomationClient proxmoxAutomationClient,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var host = instance.InternalIp?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            host = await ResolveVmInternalIpAsync(instance.VmId, proxmoxAutomationClient, logger, cancellationToken);
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new InvalidOperationException("VM internal IP is missing. Cannot use private SSH backup path.");
            }
        }

        try
        {
            using var json = JsonDocument.Parse(instance.ConfigurationJson);
            if (!json.RootElement.TryGetProperty("systemAccess", out var access))
            {
                throw new InvalidOperationException("Missing VM systemAccess credentials in configuration.");
            }

            var username = access.TryGetProperty("username", out var usernameProp) && usernameProp.ValueKind == JsonValueKind.String
                ? usernameProp.GetString()
                : null;
            var encrypted = access.TryGetProperty("passwordEncrypted", out var passProp) && passProp.ValueKind == JsonValueKind.String
                ? passProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(encrypted))
            {
                throw new InvalidOperationException("VM systemAccess credentials are invalid.");
            }

            return (host, username.Trim(), SecureSecret.Decrypt(encrypted));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("VM configuration JSON is invalid.", ex);
        }
    }

    public static async Task<string?> ResolveVmInternalIpAsync(
        int vmId,
        ProxmoxAutomationClient proxmoxAutomationClient,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            var ip = await TryResolveVmInternalIpWithFallbackAsync(vmId, proxmoxAutomationClient, logger, cancellationToken);
            if (!string.IsNullOrWhiteSpace(ip))
            {
                logger.LogInformation("Resolved VM internal IP from guest agent vmId={VmId} ip={Ip} attempt={Attempt}", vmId, ip, attempt);
                return ip.Trim();
            }

            if (attempt < 6)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        return null;
    }

    public static async Task<string?> TryResolveVmInternalIpWithFallbackAsync(
        int vmId,
        ProxmoxAutomationClient proxmoxAutomationClient,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var ip = await proxmoxAutomationClient.TryGetVmGuestIpv4Async(vmId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(ip))
            {
                return ip;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Primary guest-agent IP read failed vmId={VmId}; trying command fallback.", vmId);
        }

        try
        {
            const string script = """
set -e
IP="$(ip -4 -o addr show scope global | awk '{print $4}' | cut -d/ -f1 | head -n1 || true)"
echo "PLAYMOX_FALLBACK_IP=$IP"
""";
            var result = await proxmoxAutomationClient.RunGuestScriptForOutputAsync(
                vmId,
                script,
                TimeSpan.FromMinutes(2),
                cancellationToken);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StdOut))
            {
                return null;
            }

            var values = ParseScriptOutputValues(result.StdOut);
            values.TryGetValue("PLAYMOX_FALLBACK_IP", out var fallbackIp);
            return string.IsNullOrWhiteSpace(fallbackIp) ? null : fallbackIp.Trim();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fallback guest command IP read failed vmId={VmId}.", vmId);
            return null;
        }
    }

    public static (string? Username, string? Password) ExtractSteamAccess(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return (null, null);
        }

        try
        {
            using var json = JsonDocument.Parse(configurationJson);
            if (!json.RootElement.TryGetProperty("systemSteam", out var steam))
            {
                return (null, null);
            }

            var username = steam.TryGetProperty("username", out var usernameProp) && usernameProp.ValueKind == JsonValueKind.String
                ? usernameProp.GetString()
                : null;
            var encrypted = steam.TryGetProperty("passwordEncrypted", out var passProp) && passProp.ValueKind == JsonValueKind.String
                ? passProp.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(username))
            {
                return (null, null);
            }

            var normalizedUsername = username.Trim();
            if (string.Equals(normalizedUsername, "anonymous", StringComparison.OrdinalIgnoreCase))
            {
                return ("anonymous", null);
            }

            if (string.IsNullOrWhiteSpace(encrypted))
            {
                return (normalizedUsername, null);
            }

            return (normalizedUsername, SecureSecret.Decrypt(encrypted));
        }
        catch
        {
            return (null, null);
        }
    }

    public static string NormalizeConfigurationJson(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return "{}";
        }

        try
        {
            using var json = JsonDocument.Parse(configurationJson);
            var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("user", out var user) &&
                user.ValueKind == JsonValueKind.Object)
            {
                root = user;
            }

            return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return "{}";
        }
    }

    public static Dictionary<string, string> ParseScriptOutputValues(string? output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(output))
        {
            return result;
        }

        var lines = output.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var separator = line.IndexOf('=');
            if (separator <= 0 || separator >= line.Length - 1)
            {
                continue;
            }

            var key = line[..separator];
            if (!key.StartsWith("PLAYMOX_", StringComparison.Ordinal))
            {
                continue;
            }

            result[key] = line[(separator + 1)..];
        }

        return result;
    }

    public static bool ParseBoolOrDefault(string? value, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" => true,
            "false" or "0" or "no" => false,
            _ => fallback
        };
    }

    public static string EscapeForSingleQuotes(string value)
        => (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("'", "'\"'\"'", StringComparison.Ordinal);

    public static string SanitizeSingleLineConfigValue(string? value)
        => (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("$", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Trim();

    public static string EscapeForDoubleQuotedConfig(string? value)
        => SanitizeSingleLineConfigValue(value)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    public static string EscapeForJson(string? value)
        => (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

    public static string? CompactDiagnosticsForMessage(string? diagnostics)
    {
        if (string.IsNullOrWhiteSpace(diagnostics))
        {
            return null;
        }

        var compact = diagnostics
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('\n', ' ')
            .Trim();

        if (compact.Length <= 700)
        {
            return compact;
        }

        return $"{compact[..700]}...";
    }
}
