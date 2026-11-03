using System.Net;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using PlaymoxAPI.BLL;
using PlaymoxAPI.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PlaymoxAPI.Services.Proxmox;

public class ProxmoxAutomationClient
{
    private readonly ILogger<ProxmoxAutomationClient> logger;
    private static readonly TimeSpan LiveSnapshotRetention = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<int, GuestScriptLiveSnapshot> LiveGuestScripts = new();
    private static readonly ConcurrentDictionary<int, ConcurrentQueue<GuestScriptLiveSnapshot>> LiveGuestScriptHistory = new();

    public ProxmoxAutomationClient(ILogger<ProxmoxAutomationClient> logger)
    {
        this.logger = logger;
    }

    public async Task<int> GetNextVmIdAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var json = await GetJsonAsync(connection.Client, $"{connection.BaseUrl}/cluster/nextid", cancellationToken);
        var value = GetString(json.RootElement, "data");
        if (!int.TryParse(value, out var vmid))
        {
            throw new InvalidOperationException("Proxmox did not return a valid VMID from /cluster/nextid.");
        }

        return vmid;
    }

    public async Task<int?> GetNodeCpuCoreCountAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var json = await GetJsonAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/status",
            cancellationToken);

        var cpus = GetString(json.RootElement, "data", "cpuinfo", "cpus");
        if (int.TryParse(cpus, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        return null;
    }

    public async Task<string?> CloneFromTemplateAsync(int targetVmId, string name, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        var form = new Dictionary<string, string?>
        {
            ["newid"] = targetVmId.ToString(),
            ["name"] = name,
            ["target"] = connection.Node,
            ["full"] = "1"
        };

        return await PostFormForDataAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{connection.TemplateVmid}/clone",
            form,
            cancellationToken);
    }

    public async Task<string?> SetVmResourcesAsync(
        int vmId,
        int cpuCores,
        int memoryMb,
        CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        var form = new Dictionary<string, string?>
        {
            ["cores"] = cpuCores.ToString(),
            ["memory"] = memoryMb.ToString()
        };

        return await PostFormForDataAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/config",
            form,
            cancellationToken);
    }

    public async Task<string?> SetVmCloudInitAsync(
        int vmId,
        string cloudInitUser,
        string cloudInitPassword,
        CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        var form = new Dictionary<string, string?>
        {
            ["ciuser"] = cloudInitUser,
            ["cipassword"] = cloudInitPassword,
            ["ipconfig0"] = "ip=dhcp",
            ["ciupgrade"] = "0"
        };

        return await PostFormForDataAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/config",
            form,
            cancellationToken);
    }

    public async Task<string?> EnsureVmNetworkAsync(int vmId, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        var form = new Dictionary<string, string?>
        {
            ["net0"] = $"virtio,bridge={connection.Bridge}"
        };

        return await PostFormForDataAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/config",
            form,
            cancellationToken);
    }

    public async Task<string?> SetVmConsoleModeAsync(int vmId, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        var form = new Dictionary<string, string?>
        {
            ["vga"] = "std",
            ["delete"] = "serial0"
        };

        return await PostFormForDataAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/config",
            form,
            cancellationToken);
    }

    public async Task<string?> RegenerateCloudInitAsync(int vmId, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        try
        {
            return await PostFormForDataAsync(
                connection.Client,
                $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/cloudinit",
                new Dictionary<string, string?>(),
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("not implemented", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("HTTP 501", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
    }

    public async Task<string?> ResizeVmDiskAsync(int vmId, int sizeDeltaGb, CancellationToken cancellationToken = default)
    {
        if (sizeDeltaGb <= 0)
        {
            return null;
        }

        using var connection = await CreateConnectionAsync(cancellationToken);
        var form = new Dictionary<string, string?>
        {
            ["disk"] = "scsi0",
            ["size"] = $"+{sizeDeltaGb}G"
        };

        return await PutFormForDataAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/resize",
            form,
            cancellationToken);
    }

    public async Task<string?> StartVmAsync(int vmId, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var statusResponse = await connection.Client.GetAsync(
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/status/current",
            cancellationToken);
        if ((int)statusResponse.StatusCode is >= 200 and < 300)
        {
            var rawStatus = await statusResponse.Content.ReadAsStringAsync(cancellationToken);
            using var statusJson = JsonDocument.Parse(rawStatus);
            var status = GetString(statusJson.RootElement, "data", "status");
            if (string.Equals(status, "running", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return await PostFormForDataAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/status/start",
            new Dictionary<string, string?>(),
            cancellationToken);
    }

    public async Task<string?> StopVmAsync(int vmId, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        return await PostFormForDataAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/status/stop",
            new Dictionary<string, string?>(),
            cancellationToken);
    }

    public async Task<string?> RestartVmAsync(int vmId, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        return await PostFormForDataAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/status/reboot",
            new Dictionary<string, string?>(),
            cancellationToken);
    }

    public async Task<string?> ShutdownVmAsync(int vmId, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        return await PostFormForDataAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/status/shutdown",
            new Dictionary<string, string?>(),
            cancellationToken);
    }

    public async Task<string?> SetVmAutostartAsync(int vmId, bool enabled, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        return await PostFormForDataAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/config",
            new Dictionary<string, string?>
            {
                ["onboot"] = enabled ? "1" : "0"
            },
            cancellationToken);
    }

    public async Task<string?> SetVmGuestUserPasswordAsync(
        int vmId,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        var form = new Dictionary<string, string?>
        {
            ["username"] = username,
            ["password"] = password,
            ["crypted"] = "0"
        };

        return await PostFormForDataAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/agent/set-user-password",
            form,
            cancellationToken);
    }

    public async Task SyncVmFirewallRulesAsync(
        int vmId,
        int? sshPort,
        int? gamePortStart,
        int? gamePortCount,
        string? gameProtocol,
        CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);

        await PutFormForDataAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/firewall/options",
            new Dictionary<string, string?>
            {
                ["enable"] = "1",
                ["policy_in"] = "DROP",
                ["policy_out"] = "ACCEPT"
            },
            cancellationToken);

        using var rulesJson = await GetJsonAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/firewall/rules",
            cancellationToken);

        var positions = new List<int>();
        if (rulesJson.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var rule in data.EnumerateArray())
            {
                var posText = GetString(rule, "pos");
                if (int.TryParse(posText, out var pos))
                {
                    positions.Add(pos);
                }
            }
        }

        foreach (var pos in positions.OrderByDescending(x => x))
        {
            await DeleteForDataAsync(
                connection.Client,
                $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/firewall/rules/{pos}",
                cancellationToken);
        }

        if (sshPort.HasValue)
        {
            await PostFormForDataAsync(
                connection.Client,
                $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/firewall/rules",
                new Dictionary<string, string?>
                {
                    ["type"] = "in",
                    ["action"] = "ACCEPT",
                    ["proto"] = "tcp",
                    ["dport"] = sshPort.Value.ToString(),
                    ["comment"] = "Playmox SSH"
                },
                cancellationToken);
        }

        if (gamePortStart.HasValue && gamePortCount.GetValueOrDefault() > 0)
        {
            var protocols = ParseGameProtocols(gameProtocol);
            var start = gamePortStart.Value;
            var end = start + gamePortCount!.Value - 1;
            var dport = gamePortCount.Value == 1 ? start.ToString() : $"{start}:{end}";
            foreach (var protocol in protocols)
            {
                await PostFormForDataAsync(
                    connection.Client,
                    $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/firewall/rules",
                    new Dictionary<string, string?>
                    {
                        ["type"] = "in",
                        ["action"] = "ACCEPT",
                        ["proto"] = protocol,
                        ["dport"] = dport,
                        ["comment"] = $"Playmox Game {protocol}"
                    },
                    cancellationToken);
            }
        }
    }

    public async Task<string?> ConfigureVmDnatAsync(
        int vmId,
        int? sshPort,
        int? gamePortStart,
        int? gamePortCount,
        string? gameProtocol,
        CancellationToken cancellationToken = default)
    {
        var hasSsh = sshPort.HasValue;
        var hasGame = gamePortStart.HasValue && gamePortCount.GetValueOrDefault() > 0;
        if (!hasSsh && !hasGame)
        {
            return null;
        }

        var protocols = ParseGameProtocols(gameProtocol);
        var protocol = protocols[0];
        var protocolExtra = protocols.Count > 1
            ? string.Join(",", protocols.Skip(1))
            : null;
        var description = BuildPlaymoxDnatDescription(
            sshPort,
            gamePortStart,
            hasGame ? gamePortCount : null,
            protocol,
            protocolExtra);

        using var connection = await CreateConnectionAsync(cancellationToken);
        var configUrl = $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/config";
        try
        {
            return await PostFormForDataAsync(
                connection.Client,
                configUrl,
                new Dictionary<string, string?>
                {
                    ["hookscript"] = "local:snippets/playmox-dnat-hook.sh",
                    ["description"] = description
                },
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("only root can set 'hookscript' config", StringComparison.OrdinalIgnoreCase))
        {
            // Some Proxmox versions/ACL combinations restrict hookscript to root only.
            // Fallback to metadata-only mode; host-side reconcile timer still applies DNAT.
            return await PostFormForDataAsync(
                connection.Client,
                configUrl,
                new Dictionary<string, string?>
                {
                    ["description"] = description
                },
                cancellationToken);
        }
    }

    public async Task WaitForGuestAgentAsync(
        int vmId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        var url = $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/agent/ping";
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            try
            {
                await PostFormForDataAsync(connection.Client, url, new Dictionary<string, string?>(), cancellationToken);
                return;
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("QEMU guest agent is not running", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("guest agent", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        throw new TimeoutException("Timed out waiting for QEMU guest agent.");
    }

    public async Task<string?> TryGetVmGuestIpv4Async(int vmId, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var json = await GetJsonAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/agent/network-get-interfaces",
            cancellationToken);

        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var candidates = new List<(string Ip, string InterfaceName, int Score)>();
        foreach (var iface in data.EnumerateArray())
        {
            var interfaceName = GetString(iface, "name")?.Trim();
            if (string.IsNullOrWhiteSpace(interfaceName) ||
                string.Equals(interfaceName, "lo", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!iface.TryGetProperty("ip-addresses", out var addresses) || addresses.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var interfaceScore = ScoreInterfaceForGuestIp(interfaceName);
            foreach (var addr in addresses.EnumerateArray())
            {
                var type = GetString(addr, "ip-address-type");
                var ip = GetString(addr, "ip-address");
                if (!string.Equals(type, "ipv4", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(ip) ||
                    !IPAddress.TryParse(ip, out var parsedIp) ||
                    parsedIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
                    ip.StartsWith("127.", StringComparison.Ordinal) ||
                    ip.StartsWith("169.254.", StringComparison.Ordinal) ||
                    ip.StartsWith("0.", StringComparison.Ordinal))
                {
                    continue;
                }

                var score = interfaceScore + ScoreIpv4ForGuestIp(parsedIp);
                candidates.Add((ip.Trim(), interfaceName, score));
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var selected = candidates
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.InterfaceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Ip, StringComparer.OrdinalIgnoreCase)
            .First();
        logger.LogDebug(
            "Selected guest IPv4 vmId={VmId} ip={Ip} iface={Iface} score={Score} candidates={CandidateCount}",
            vmId,
            selected.Ip,
            selected.InterfaceName,
            selected.Score,
            candidates.Count);
        return selected.Ip;
    }

    private static int ScoreInterfaceForGuestIp(string interfaceName)
    {
        var name = interfaceName.Trim().ToLowerInvariant();
        if (name.StartsWith("en", StringComparison.Ordinal) ||
            name.StartsWith("eth", StringComparison.Ordinal) ||
            name.StartsWith("bond", StringComparison.Ordinal))
        {
            return 300;
        }

        if (name.StartsWith("wl", StringComparison.Ordinal))
        {
            return 260;
        }

        if (name.StartsWith("docker", StringComparison.Ordinal) ||
            name.StartsWith("br-", StringComparison.Ordinal) ||
            name.StartsWith("veth", StringComparison.Ordinal) ||
            name.StartsWith("virbr", StringComparison.Ordinal) ||
            name.StartsWith("cni", StringComparison.Ordinal) ||
            name.StartsWith("flannel", StringComparison.Ordinal) ||
            name.StartsWith("kube", StringComparison.Ordinal) ||
            name.StartsWith("podman", StringComparison.Ordinal) ||
            name.StartsWith("zt", StringComparison.Ordinal) ||
            name.StartsWith("tailscale", StringComparison.Ordinal) ||
            name.StartsWith("tun", StringComparison.Ordinal) ||
            name.StartsWith("tap", StringComparison.Ordinal) ||
            name.StartsWith("wg", StringComparison.Ordinal))
        {
            return 20;
        }

        return 180;
    }

    private static int ScoreIpv4ForGuestIp(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return 0;
        }

        if (bytes[0] == 10 ||
            (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168))
        {
            return 150;
        }

        if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
        {
            return 90;
        }

        return 40;
    }

    public async Task<bool> TryPinGuestDhcpAddressAsStaticAsync(
        int vmId,
        string expectedIpv4,
        CancellationToken cancellationToken = default)
    {
        if (!IPAddress.TryParse(expectedIpv4, out var parsedIp) || parsedIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var script = """
set -e
# PLAYMOX_SUMMARY: Pin guest DHCP IP as static
EXPECTED_IP="__PLAYMOX_EXPECTED_IP__"
IFACE="$(ip -4 -o addr show scope global | awk '{print $2}' | head -n1 || true)"
if [ -z "$IFACE" ]; then
  echo "PLAYMOX_PIN_RESULT=skip-no-interface"
  exit 0
fi
CURRENT_ADDR="$(ip -4 -o addr show dev "$IFACE" scope global | awk '{print $4}' | head -n1 || true)"
CURRENT_IP="${CURRENT_ADDR%%/*}"
PREFIX="${CURRENT_ADDR##*/}"
if [ -z "$CURRENT_IP" ] || [ "$CURRENT_IP" != "$EXPECTED_IP" ]; then
  echo "PLAYMOX_PIN_RESULT=skip-ip-mismatch"
  exit 0
fi
if [ -z "$PREFIX" ]; then
  echo "PLAYMOX_PIN_RESULT=skip-missing-prefix"
  exit 0
fi
GATEWAY="$(ip route show default dev "$IFACE" 2>/dev/null | awk '{print $3}' | head -n1 || true)"
if [ -z "$GATEWAY" ]; then
  GATEWAY="$(ip route show default 2>/dev/null | awk '{print $3}' | head -n1 || true)"
fi
if [ -z "$GATEWAY" ]; then
  echo "PLAYMOX_PIN_RESULT=skip-missing-gateway"
  exit 0
fi
DNS_LIST="$(awk '/^nameserver / {print $2}' /etc/resolv.conf 2>/dev/null | head -n3 | xargs || true)"
mkdir -p /etc/netplan
cat >/etc/netplan/70-playmox-static.yaml <<EOF
network:
  version: 2
  ethernets:
    $IFACE:
      dhcp4: false
      addresses:
        - $CURRENT_IP/$PREFIX
      routes:
        - to: default
          via: $GATEWAY
EOF
if [ -n "$DNS_LIST" ]; then
  printf '%s\n' "      nameservers:" >> /etc/netplan/70-playmox-static.yaml
  printf '%s\n' "        addresses: [${DNS_LIST// /, }]" >> /etc/netplan/70-playmox-static.yaml
fi
netplan generate
netplan apply
echo "PLAYMOX_PIN_RESULT=applied"
""".Replace("__PLAYMOX_EXPECTED_IP__", expectedIpv4.Trim(), StringComparison.Ordinal);

        var result = await RunGuestScriptForOutputAsync(vmId, script, TimeSpan.FromMinutes(3), cancellationToken);
        if (result.ExitCode != 0)
        {
            return false;
        }

        var marker = "PLAYMOX_PIN_RESULT=";
        var lines = (result.StdOut ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var status = lines.FirstOrDefault(x => x.StartsWith(marker, StringComparison.OrdinalIgnoreCase));
        return string.Equals(status?[marker.Length..], "applied", StringComparison.OrdinalIgnoreCase);
    }

    public async Task RunGuestScriptAsync(
        int vmId,
        string script,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var result = await RunGuestScriptForOutputAsync(vmId, script, timeout, cancellationToken);
        if (result.ExitCode == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Guest script failed (exit code {result.ExitCode}). stdout: {result.StdOut ?? "-"} stderr: {result.StdErr ?? "-"}");
    }

    public async Task<GuestScriptExecutionResult> RunGuestScriptForOutputAsync(
        int vmId,
        string script,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var normalizedScript = script
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

        using var connection = await CreateConnectionAsync(cancellationToken);
        var execUrl = $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/agent/exec";

        using var execJson = await ExecuteGuestExecAsync(
            connection.Client,
            execUrl,
            normalizedScript,
            cancellationToken);

        var pidText = GetString(execJson.RootElement, "data", "pid");
        if (!int.TryParse(pidText, out var pid))
        {
            throw new InvalidOperationException("Proxmox did not return guest exec PID.");
        }

        var statusUrl = $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/agent/exec-status?pid={pid}";
        var started = DateTime.UtcNow;
        var snapshot = new GuestScriptLiveSnapshot
        {
            VmId = vmId,
            Pid = pid,
            IsRunning = true,
            StartedAtUtc = started,
            UpdatedAtUtc = started,
            CommandSummary = SummarizeScript(normalizedScript)
        };
        LiveGuestScripts[vmId] = snapshot;
        EnqueueLiveSnapshot(snapshot);
        logger.LogInformation("QGA exec started vmId={VmId} pid={Pid} timeout={TimeoutSeconds}s", vmId, pid, timeout.TotalSeconds);
        while (DateTime.UtcNow - started < timeout)
        {
            JsonDocument statusJson;
            try
            {
                statusJson = await GetJsonAsync(connection.Client, statusUrl, cancellationToken);
            }
            catch (InvalidOperationException ex) when (IsGuestExecStatusTimeoutError(ex))
            {
                logger.LogWarning("QGA exec-status transient timeout vmId={VmId} pid={Pid}; retrying", vmId, pid);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }

            using (statusJson)
            {
            var exitedText = GetString(statusJson.RootElement, "data", "exited");
            var exited = string.Equals(exitedText, "true", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(exitedText, "1", StringComparison.OrdinalIgnoreCase);

            snapshot.StdOut = GetString(statusJson.RootElement, "data", "out-data");
            snapshot.StdErr = GetString(statusJson.RootElement, "data", "err-data");
            snapshot.UpdatedAtUtc = DateTime.UtcNow;
            LiveGuestScripts[vmId] = snapshot;

            if (!exited)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }

            var exitCodeText = GetString(statusJson.RootElement, "data", "exitcode") ?? "1";
            if (!int.TryParse(exitCodeText, out var exitCode))
            {
                exitCode = 1;
            }

            snapshot.IsRunning = false;
            snapshot.ExitCode = exitCode;
            snapshot.CompletedAtUtc = DateTime.UtcNow;
            LiveGuestScripts[vmId] = snapshot;

            return new GuestScriptExecutionResult
            {
                ExitCode = exitCode,
                StdOut = GetString(statusJson.RootElement, "data", "out-data"),
                StdErr = GetString(statusJson.RootElement, "data", "err-data")
            };
            }
        }

        snapshot.IsRunning = false;
        snapshot.ExitCode = null;
        snapshot.StdErr = string.IsNullOrWhiteSpace(snapshot.StdErr)
            ? "Timed out waiting for guest script execution."
            : snapshot.StdErr;
        snapshot.CompletedAtUtc = DateTime.UtcNow;
        snapshot.UpdatedAtUtc = DateTime.UtcNow;
        LiveGuestScripts[vmId] = snapshot;

        throw new TimeoutException("Timed out waiting for guest script execution.");
    }

    public GuestScriptLiveSnapshot? GetLiveGuestScriptSnapshot(int vmId)
    {
        PruneLiveHistory(vmId, DateTime.UtcNow);
        if (!LiveGuestScripts.TryGetValue(vmId, out var snapshot))
        {
            return null;
        }

        if (IsSnapshotExpired(snapshot, DateTime.UtcNow))
        {
            LiveGuestScripts.TryRemove(vmId, out _);
            return null;
        }

        return snapshot;
    }

    public IReadOnlyList<GuestScriptLiveSnapshot> GetRecentGuestScriptSnapshots(int vmId)
    {
        var now = DateTime.UtcNow;
        PruneLiveHistory(vmId, now);
        if (!LiveGuestScriptHistory.TryGetValue(vmId, out var queue))
        {
            return [];
        }

        return queue.ToArray()
            .Where(x => !IsSnapshotExpired(x, now))
            .OrderBy(x => x.StartedAtUtc)
            .ToList();
    }

    private static bool IsGuestExecStatusTimeoutError(InvalidOperationException ex)
    {
        if (!ex.Message.Contains("guest-exec-status", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ex.Message.Contains("got timeout", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("PID", StringComparison.OrdinalIgnoreCase) &&
               ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnqueueLiveSnapshot(GuestScriptLiveSnapshot snapshot)
    {
        var queue = LiveGuestScriptHistory.GetOrAdd(snapshot.VmId, _ => new ConcurrentQueue<GuestScriptLiveSnapshot>());
        queue.Enqueue(snapshot);
        PruneLiveHistory(snapshot.VmId, DateTime.UtcNow);
    }

    private static void PruneLiveHistory(int vmId, DateTime nowUtc)
    {
        if (!LiveGuestScriptHistory.TryGetValue(vmId, out var queue))
        {
            return;
        }

        while (queue.TryPeek(out var head) && IsSnapshotExpired(head, nowUtc))
        {
            queue.TryDequeue(out _);
        }

        if (queue.IsEmpty)
        {
            LiveGuestScriptHistory.TryRemove(vmId, out _);
        }

        if (LiveGuestScripts.TryGetValue(vmId, out var latest) && IsSnapshotExpired(latest, nowUtc))
        {
            LiveGuestScripts.TryRemove(vmId, out _);
        }
    }

    private static bool IsSnapshotExpired(GuestScriptLiveSnapshot snapshot, DateTime nowUtc)
    {
        if (snapshot.IsRunning)
        {
            return false;
        }

        var timestamp = snapshot.CompletedAtUtc ?? snapshot.UpdatedAtUtc;
        return nowUtc - timestamp > LiveSnapshotRetention;
    }

    private static string SummarizeScript(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return "guest-exec";
        }

        var lines = script
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            const string summaryPrefix = "# PLAYMOX_SUMMARY:";
            if (line.StartsWith(summaryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var explicitSummary = line[summaryPrefix.Length..].Trim();
                if (!string.IsNullOrWhiteSpace(explicitSummary))
                {
                    return explicitSummary.Length > 140 ? $"{explicitSummary[..140]}..." : explicitSummary;
                }
            }
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (IsBoilerplateSummaryLine(line))
            {
                continue;
            }

            if (line.Length > 140)
            {
                return $"{line[..140]}...";
            }

            return line;
        }

        var fallback = lines.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(fallback))
        {
            return "guest-exec";
        }

        return fallback.Length > 140 ? $"{fallback[..140]}..." : fallback;
    }

    private static bool IsBoilerplateSummaryLine(string line)
    {
        if (line.Length == 0)
        {
            return true;
        }

        if (line.StartsWith('#'))
        {
            return true;
        }

        if (string.Equals(line, "set -e", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(line, "set -eu", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Ignore the common apt-cache freshness guard prelude to show meaningful command summary.
        if (line.StartsWith("if [ ! -f /var/lib/apt/periodic/update-success-stamp ]", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(line, "fi", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("update-success-stamp -mmin +60", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("apt-get -o DPkg::Lock::Timeout=300 update -y", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("apt-get update -y", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public async Task<string?> DeleteVmAsync(int vmId, CancellationToken cancellationToken = default)
    {
        if (await VmExistsAsync(vmId, cancellationToken))
        {
            var isManaged = await IsManagedPlaymoxVmAsync(vmId, cancellationToken);
            if (!isManaged)
            {
                throw new InvalidOperationException(
                    $"Safety check failed: Proxmox VM {vmId} exists but is NOT managed by Playmox (missing 'playmox-' name prefix or description tag). Deletion permanently blocked.");
            }
        }

        using var connection = await CreateConnectionAsync(cancellationToken);
        try
        {
            return await DeleteForDataAsync(
                connection.Client,
                $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}?purge=1&destroy-unreferenced-disks=1",
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("qemu-server", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
    }

    public async Task<string> GetVmStatusAsync(int vmId, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var json = await GetJsonAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/status/current",
            cancellationToken);
        return GetString(json.RootElement, "data", "status") ?? "unknown";
    }

    public async Task<int?> GetVmPrimaryDiskSizeGbAsync(int vmId, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var json = await GetJsonAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/config",
            cancellationToken);

        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var scsi0 = GetString(data, "scsi0");
        return ParseDiskSizeGb(scsi0);
    }

    public async Task<bool> VmExistsAsync(int vmId, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var response = await connection.Client.GetAsync(
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/status/current",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        if ((int)response.StatusCode is >= 200 and < 300)
        {
            return true;
        }

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (raw.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("qemu-server", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        EnsureSuccess(response.StatusCode, $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/status/current", raw);
        return false;
    }

    public async Task<bool> IsManagedPlaymoxVmAsync(int vmId, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        using var json = await GetJsonAsync(
            connection.Client,
            $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/config",
            cancellationToken);

        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var vmName = GetString(data, "name");
        if (!string.IsNullOrWhiteSpace(vmName) &&
            vmName.StartsWith("playmox-", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var description = GetString(data, "description");
        return !string.IsNullOrWhiteSpace(description) &&
               description.Contains("playmox-dnat:", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<VmDiagnosticsDto> GetVmDiagnosticsAsync(int vmId, CancellationToken cancellationToken = default)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        var configUrl = $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/config";
        var statusUrl = $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/status/current";
        var cloudInitUrl = $"{connection.BaseUrl}/nodes/{connection.Node}/qemu/{vmId}/cloudinit/dump?type=network";

        var configRaw = await GetRawAsync(connection.Client, configUrl, cancellationToken);
        var statusRaw = await GetRawAsync(connection.Client, statusUrl, cancellationToken);
        var cloudInitRaw = await GetRawAsync(connection.Client, cloudInitUrl, cancellationToken);

        return new VmDiagnosticsDto
        {
            VmId = vmId,
            ConfigJson = configRaw,
            StatusJson = statusRaw,
            CloudInitNetwork = cloudInitRaw
        };
    }

    public async Task WaitForTaskAsync(string upid, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(upid))
        {
            return;
        }

        using var connection = await CreateConnectionAsync(cancellationToken);
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            using var json = await GetJsonAsync(
                connection.Client,
                $"{connection.BaseUrl}/nodes/{connection.Node}/tasks/{Uri.EscapeDataString(upid)}/status",
                cancellationToken);

            var status = GetString(json.RootElement, "data", "status");
            if (!string.Equals(status, "stopped", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                continue;
            }

            var exitStatus = GetString(json.RootElement, "data", "exitstatus");
            if (upid.Contains(":qmstart:", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(exitStatus) &&
                exitStatus.Contains("already running", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(exitStatus, "OK", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            throw new InvalidOperationException($"Proxmox task failed: {upid}, exit status: {exitStatus ?? "unknown"}");
        }

        throw new TimeoutException($"Timed out waiting for Proxmox task completion: {upid}");
    }

    private static async Task<ProxmoxConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        using var dbContext = new PlaymoxContext();
        var settings = await dbContext.ProxmoxSettings
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (settings == null)
        {
            throw new InvalidOperationException("Proxmox configuration is missing.");
        }

        if (string.IsNullOrWhiteSpace(settings.ApiTokenSecret))
        {
            throw new InvalidOperationException("Proxmox API token secret is missing.");
        }

        var tokenSecret = SecureSecret.Decrypt(settings.ApiTokenSecret);
        if (string.IsNullOrWhiteSpace(tokenSecret))
        {
            throw new InvalidOperationException("Proxmox API token secret cannot be decrypted.");
        }

        var baseUrl = settings.ProxmoxUrl.TrimEnd('/');
        if (!baseUrl.EndsWith("/api2/json", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = $"{baseUrl}/api2/json";
        }

        var handler = new HttpClientHandler();
        if (Config.AllowSelfSignedProxmoxCertificates)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        var tokenHeader = $"PVEAPIToken={settings.ApiUser}!{settings.ApiTokenId}={tokenSecret}";
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", tokenHeader);

        return new ProxmoxConnection(
            client,
            handler,
            baseUrl,
            settings.ProxmoxNode,
            settings.TemplateVmid,
            settings.Bridge);
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient httpClient, string url, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response.StatusCode, url, raw);
        return JsonDocument.Parse(raw);
    }

    private static async Task<string> GetRawAsync(HttpClient httpClient, string url, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if ((int)response.StatusCode is >= 200 and < 300)
        {
            return raw;
        }

        return $"HTTP {(int)response.StatusCode}: {raw}";
    }

    private static async Task<string?> PostFormForDataAsync(
        HttpClient httpClient,
        string url,
        Dictionary<string, string?> payload,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(payload
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => new KeyValuePair<string, string>(x.Key, x.Value!)));
        using var response = await httpClient.PostAsync(url, content, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response.StatusCode, url, raw);
        return GetDataAsString(raw);
    }

    private static async Task<string?> PutFormForDataAsync(
        HttpClient httpClient,
        string url,
        Dictionary<string, string?> payload,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(payload
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => new KeyValuePair<string, string>(x.Key, x.Value!)));
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response.StatusCode, url, raw);
        return GetDataAsString(raw);
    }

    private static async Task<JsonDocument> PostFormForJsonAsync(
        HttpClient httpClient,
        string url,
        IEnumerable<KeyValuePair<string, string?>> payload,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(payload
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => new KeyValuePair<string, string>(x.Key, x.Value!)));
        using var response = await httpClient.PostAsync(url, content, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response.StatusCode, url, raw);
        return JsonDocument.Parse(raw);
    }

    private static async Task<JsonDocument> ExecuteGuestExecAsync(
        HttpClient httpClient,
        string execUrl,
        string script,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 6;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await PostFormForJsonAsync(httpClient, execUrl, BuildGuestExecNewSchemaPayload(script), cancellationToken);
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("\"command\"", StringComparison.OrdinalIgnoreCase) &&
                (ex.Message.Contains("property is not defined in schema", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("type check", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("parameter verification failed", StringComparison.OrdinalIgnoreCase)))
            {
                return await ExecuteGuestExecSingleCommandWithRetryAsync(
                    httpClient,
                    execUrl,
                    BuildGuestExecLegacyPayload(script),
                    maxAttempts,
                    cancellationToken);
            }
            catch (InvalidOperationException ex) when (IsTransientGuestExecError(ex) && attempt < maxAttempts)
            {
                var delaySeconds = Math.Min(2 * attempt, 10);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        // Last attempt to surface the final error context.
        return await PostFormForJsonAsync(
            httpClient,
            execUrl,
            BuildGuestExecNewSchemaPayload(script),
            cancellationToken);
    }

    private static async Task<JsonDocument> ExecuteGuestExecSingleCommandWithRetryAsync(
        HttpClient httpClient,
        string execUrl,
        List<KeyValuePair<string, string?>> payload,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await PostFormForJsonAsync(
                    httpClient,
                    execUrl,
                    payload,
                    cancellationToken);
            }
            catch (InvalidOperationException ex) when (IsTransientGuestExecError(ex) && attempt < maxAttempts)
            {
                var delaySeconds = Math.Min(2 * attempt, 10);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        return await PostFormForJsonAsync(
            httpClient,
            execUrl,
            payload,
            cancellationToken);
    }

    private static List<KeyValuePair<string, string?>> BuildGuestExecNewSchemaPayload(string script)
    {
        return
        [
            new("command", "/bin/bash"),
            new("command", "-lc"),
            new("command", script)
        ];
    }

    private static List<KeyValuePair<string, string?>> BuildGuestExecLegacyPayload(string script)
    {
        return
        [
            new("command", "/bin/bash"),
            new("arg", "-lc"),
            new("arg", script),
            new("capture-output", "1")
        ];
    }

    private static bool IsTransientGuestExecError(InvalidOperationException ex)
    {
        var message = ex.Message;
        return message.Contains("HTTP 596", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("connection reset", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("guest agent", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> DeleteForDataAsync(HttpClient httpClient, string url, CancellationToken cancellationToken)
    {
        using var response = await httpClient.DeleteAsync(url, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response.StatusCode, url, raw);
        return GetDataAsString(raw);
    }

    private static void EnsureSuccess(HttpStatusCode statusCode, string url, string raw)
    {
        if ((int)statusCode is >= 200 and < 300)
        {
            return;
        }

        throw new InvalidOperationException($"HTTP {(int)statusCode} for {url}: {raw}");
    }

    private static string? GetDataAsString(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        using var json = JsonDocument.Parse(rawJson);
        if (!json.RootElement.TryGetProperty("data", out var data))
        {
            return null;
        }

        return data.ValueKind switch
        {
            JsonValueKind.String => data.GetString(),
            JsonValueKind.Number => data.GetRawText(),
            JsonValueKind.Object or JsonValueKind.Array => data.GetRawText(),
            _ => null
        };
    }

    private static string? GetString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var node in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(node, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int? ParseDiskSizeGb(string? diskConfigValue)
    {
        if (string.IsNullOrWhiteSpace(diskConfigValue))
        {
            return null;
        }

        const string marker = "size=";
        var idx = diskConfigValue.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        var sizePart = diskConfigValue[(idx + marker.Length)..];
        var commaIdx = sizePart.IndexOf(',', StringComparison.Ordinal);
        if (commaIdx >= 0)
        {
            sizePart = sizePart[..commaIdx];
        }

        sizePart = sizePart.Trim();
        if (sizePart.Length < 2)
        {
            return null;
        }

        var unit = char.ToUpperInvariant(sizePart[^1]);
        if (!double.TryParse(sizePart[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var raw))
        {
            return null;
        }

        var gb = unit switch
        {
            'T' => raw * 1024d,
            'G' => raw,
            'M' => raw / 1024d,
            'K' => raw / (1024d * 1024d),
            _ => raw
        };

        return gb > 0 ? (int)Math.Ceiling(gb) : null;
    }

    private static string BuildPlaymoxDnatDescription(
        int? sshPort,
        int? gamePortStart,
        int? gamePortCount,
        string protocol,
        string? protocolExtra = null)
    {
        var ssh = sshPort?.ToString() ?? "-";
        var gameStart = gamePortStart?.ToString() ?? "-";
        var gameCount = gamePortCount?.ToString() ?? "-";
        var extra = string.IsNullOrWhiteSpace(protocolExtra)
            ? string.Empty
            : $";protoExtra={protocolExtra}";
        return $"playmox-dnat:ssh={ssh};gameStart={gameStart};gameCount={gameCount};proto={protocol}{extra}";
    }

    private static List<string> ParseGameProtocols(string? configured)
    {
        var raw = string.IsNullOrWhiteSpace(configured) ? "tcp" : configured;
        var protocols = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x is "tcp" or "udp")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (protocols.Count == 0)
        {
            protocols.Add("tcp");
        }

        return protocols;
    }

    private sealed class ProxmoxConnection : IDisposable
    {
        private readonly HttpClientHandler handler;
        public HttpClient Client { get; }
        public string BaseUrl { get; }
        public string Node { get; }
        public int TemplateVmid { get; }
        public string Bridge { get; }

        public ProxmoxConnection(HttpClient client, HttpClientHandler handler, string baseUrl, string node, int templateVmid, string bridge)
        {
            Client = client;
            this.handler = handler;
            BaseUrl = baseUrl;
            Node = node;
            TemplateVmid = templateVmid;
            Bridge = bridge;
        }

        public void Dispose()
        {
            Client.Dispose();
            handler.Dispose();
        }
    }
}

public class VmDiagnosticsDto
{
    public int VmId { get; set; }
    public string ConfigJson { get; set; } = string.Empty;
    public string StatusJson { get; set; } = string.Empty;
    public string CloudInitNetwork { get; set; } = string.Empty;
}

public class GuestScriptExecutionResult
{
    public int ExitCode { get; set; }
    public string? StdOut { get; set; }
    public string? StdErr { get; set; }
}

public class GuestScriptLiveSnapshot
{
    public int VmId { get; set; }
    public int Pid { get; set; }
    public string CommandSummary { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public int? ExitCode { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? StdOut { get; set; }
    public string? StdErr { get; set; }
}
