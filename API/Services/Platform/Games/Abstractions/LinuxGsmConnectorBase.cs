using Microsoft.Extensions.Logging;
using PlaymoxAPI.BLL.BO.Platform;
using PlaymoxAPI.Services.Proxmox;
using Renci.SshNet.Common;

namespace PlaymoxAPI.Services.Platform;

public abstract class LinuxGsmConnectorBase : IGameServerConnector
{
    protected readonly ProxmoxAutomationClient ProxmoxAutomationClient;
    protected readonly ILogger Logger;

    protected LinuxGsmConnectorBase(
        ProxmoxAutomationClient proxmoxAutomationClient,
        ILogger logger)
    {
        ProxmoxAutomationClient = proxmoxAutomationClient;
        Logger = logger;
    }

    public abstract GameServerKind Kind { get; }
    protected abstract string ServerBinary { get; }
    protected abstract string BackupSlug { get; }
    protected abstract string GameDisplayName { get; }

    public abstract Task InstallOrUpdateAsync(
        VirtualGameInstance instance,
        string? configurationJson,
        GameDefinition gameDefinition,
        bool forceReinstall,
        CancellationToken cancellationToken);

    public abstract Task<GameServerConfigurationSnapshot> GetConfigurationSnapshotAsync(
        VirtualGameInstance instance,
        string? storedConfigurationJson,
        bool refreshFromServer,
        CancellationToken cancellationToken);

    protected abstract string BuildRuntimeStatusScript(VirtualGameInstance instance, int gamePort);

    protected abstract string BuildBackupCreateScript(string backupPath);

    protected abstract string BuildBackupRestoreScript(VirtualGameInstance instance, string tempArchivePath);

    public virtual async Task StopForShutdownAsync(VirtualGameInstance instance, CancellationToken cancellationToken)
    {
        await ProxmoxAutomationClient.RunGuestScriptAsync(
            instance.VmId,
            LinuxGsmScriptBuilder.BuildStopForShutdownScript(),
            TimeSpan.FromMinutes(3),
            cancellationToken);
    }

    public virtual async Task RestartAfterVmResourceChangeAsync(VirtualGameInstance instance, CancellationToken cancellationToken)
    {
        await ProxmoxAutomationClient.RunGuestScriptAsync(
            instance.VmId,
            LinuxGsmScriptBuilder.BuildRestartAfterResourceChangeScript(ServerBinary),
            TimeSpan.FromMinutes(5),
            cancellationToken);
    }

    public virtual async Task<VmGameRuntimeInfo> GetRuntimeInfoAsync(VirtualGameInstance instance, CancellationToken cancellationToken)
    {
        var vmStatus = await ProxmoxAutomationClient.GetVmStatusAsync(instance.VmId, cancellationToken);
        if (!string.Equals(vmStatus, "running", StringComparison.OrdinalIgnoreCase))
        {
            return new VmGameRuntimeInfo
            {
                InstanceId = instance.Id,
                VmId = instance.VmId,
                Status = vmStatus,
                IsRunning = false,
                Message = "VM is not running."
            };
        }

        var gamePort = instance.GamePublicPortStart.GetValueOrDefault();
        var script = BuildRuntimeStatusScript(instance, gamePort);

        var result = await ProxmoxAutomationClient.RunGuestScriptForOutputAsync(
            instance.VmId,
            script,
            TimeSpan.FromMinutes(3),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            return new VmGameRuntimeInfo
            {
                InstanceId = instance.Id,
                VmId = instance.VmId,
                Status = "Error",
                IsRunning = false,
                Message = $"Failed to retrieve game status: {detail?.Trim()}"
            };
        }

        var values = GameServerConnectorSshOps.ParseScriptOutputValues(result.StdOut);
        values.TryGetValue("PLAYMOX_SERVICE_STATE", out var serviceState);
        values.TryGetValue("PLAYMOX_GAME_PORT", out var gamePortValue);
        values.TryGetValue("PLAYMOX_GAME_PORT_LISTENING", out var gamePortListening);
        values.TryGetValue("PLAYMOX_SERVER_CODE", out var serverCode);
        values.TryGetValue("PLAYMOX_MC_VERSION", out var detectedVersion);
        values.TryGetValue("PLAYMOX_JAVA_VERSION", out var javaVersion);

        var isRunning = string.Equals(serviceState, "active", StringComparison.OrdinalIgnoreCase);
        var isListening = string.Equals(gamePortListening, "yes", StringComparison.OrdinalIgnoreCase);
        var message = isRunning
            ? (string.IsNullOrWhiteSpace(serverCode)
                ? "Game server is running."
                : $"Game server is running. Direct connect code: {serverCode}")
            : "Game service is not active.";

        if (!isRunning)
        {
            var diagnostics = await ProxmoxAutomationClient.RunGuestScriptForOutputAsync(
                instance.VmId,
                LinuxGsmScriptBuilder.BuildServiceDiagnosticsScript(),
                TimeSpan.FromMinutes(2),
                cancellationToken);
            var diagnosticsRaw = string.IsNullOrWhiteSpace(diagnostics.StdOut) ? diagnostics.StdErr : diagnostics.StdOut;
            var compactDiagnostics = GameServerConnectorSshOps.CompactDiagnosticsForMessage(diagnosticsRaw);
            if (!string.IsNullOrWhiteSpace(compactDiagnostics))
            {
                Logger.LogWarning(
                    "{GameName} service is not active vmId={VmId} state={ServiceState} diagnostics={Diagnostics}",
                    GameDisplayName,
                    instance.VmId,
                    serviceState,
                    compactDiagnostics);
                message = $"Game service is not active. Diagnostics: {compactDiagnostics}";
            }
        }

        return new VmGameRuntimeInfo
        {
            InstanceId = instance.Id,
            VmId = instance.VmId,
            Status = string.IsNullOrWhiteSpace(serviceState) ? "unknown" : serviceState,
            IsRunning = isRunning,
            Message = message,
            DetectedServerVersion = string.IsNullOrWhiteSpace(detectedVersion) ? null : detectedVersion,
            JavaVersion = string.IsNullOrWhiteSpace(javaVersion) ? null : javaVersion,
            GamePort = int.TryParse(gamePortValue, out var parsedPort) ? parsedPort : null,
            IsGamePortListening = gamePort > 0 ? isListening : null,
            DirectConnectCode = string.IsNullOrWhiteSpace(serverCode) ? null : serverCode
        };
    }

    public virtual async Task<GameServerBackupArchive?> CreateBackupAsync(VirtualGameInstance instance, CancellationToken cancellationToken)
    {
        var (host, username, password) = await GameServerConnectorSshOps.ExtractSshAccessAsync(
            instance,
            ProxmoxAutomationClient,
            Logger,
            cancellationToken);
        using var ssh = GameServerConnectorSshOps.CreateSshClient(host, username, password);
        using var sftp = GameServerConnectorSshOps.CreateSftpClient(host, username, password);
        try
        {
            ssh.Connect();
            sftp.Connect();
        }
        catch (SshAuthenticationException ex)
        {
            throw new InvalidOperationException("SSH authentication to VM failed. Verify guest SSH auth settings and credentials.", ex);
        }
        catch (SshConnectionException ex)
        {
            throw new InvalidOperationException("SSH connection to VM failed. Verify API<->VM network reachability on port 22.", ex);
        }

        var backupPath = $"/tmp/playmox-{BackupSlug}-backup-vm{instance.VmId}-{DateTime.UtcNow:yyyyMMddHHmmss}.tar.gz";
        var backupScript = BuildBackupCreateScript(backupPath);
        using var backupCmd = ssh.CreateCommand(GameServerConnectorSshOps.NormalizeShellCommand(backupScript));
        backupCmd.CommandTimeout = TimeSpan.FromMinutes(20);
        backupCmd.Execute();
        if (backupCmd.ExitStatus != 0)
        {
            throw new InvalidOperationException($"{GameDisplayName} backup command failed: {backupCmd.Error}");
        }

        if (!sftp.Exists(backupPath))
        {
            throw new InvalidOperationException($"{GameDisplayName} backup archive was not created.");
        }

        var attrs = sftp.GetAttributes(backupPath);
        var backupSize = attrs.Size;
        const long maxDownloadBytes = 5L * 1024 * 1024 * 1024;
        Logger.LogInformation(
            "Prepared {GameName} backup vmId={VmId} host={Host} file={BackupPath} size={BackupSizeBytes}B",
            GameDisplayName,
            instance.VmId,
            host,
            backupPath,
            backupSize);
        if (backupSize > maxDownloadBytes)
        {
            if (sftp.Exists(backupPath))
            {
                sftp.DeleteFile(backupPath);
            }

            throw new InvalidOperationException("Backup archive is too large for browser download (limit 5120 MB).");
        }

        using var archiveStream = new MemoryStream(backupSize > int.MaxValue ? 0 : (int)backupSize);
        using (var remote = sftp.OpenRead(backupPath))
        {
            var buffer = new byte[1024 * 1024];
            int bytesRead;
            while ((bytesRead = remote.Read(buffer, 0, buffer.Length)) > 0)
            {
                await archiveStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }
        }

        var extension = backupPath.EndsWith(".tar.zst", StringComparison.OrdinalIgnoreCase)
            ? "tar.zst"
            : backupPath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
                ? "tar.gz"
                : "tar";
        try
        {
            if (sftp.Exists(backupPath))
            {
                sftp.DeleteFile(backupPath);
            }

            return new GameServerBackupArchive
            {
                FileName = $"playmox-{BackupSlug}-vm{instance.VmId}-{DateTime.UtcNow:yyyyMMddHHmmss}.{extension}",
                ContentType = "application/gzip",
                Content = archiveStream.ToArray()
            };
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException("Failed to prepare backup archive.", ex);
        }
    }

    public virtual async Task RestoreBackupAsync(
        VirtualGameInstance instance,
        string archiveFilePath,
        long archiveSizeBytes,
        CancellationToken cancellationToken)
    {
        if (archiveSizeBytes > 500L * 1024 * 1024)
        {
            throw new InvalidOperationException("Backup archive is too large. Max size is 500MB.");
        }

        var (host, username, password) = await GameServerConnectorSshOps.ExtractSshAccessAsync(
            instance,
            ProxmoxAutomationClient,
            Logger,
            cancellationToken);
        using var ssh = GameServerConnectorSshOps.CreateSshClient(host, username, password);
        using var sftp = GameServerConnectorSshOps.CreateSftpClient(host, username, password);
        try
        {
            ssh.Connect();
            sftp.Connect();
        }
        catch (SshAuthenticationException ex)
        {
            throw new InvalidOperationException("SSH authentication to VM failed. Verify guest SSH auth settings and credentials.", ex);
        }
        catch (SshConnectionException ex)
        {
            throw new InvalidOperationException("SSH connection to VM failed. Verify API<->VM network reachability on port 22.", ex);
        }

        var tempArchivePath = $"/tmp/playmox-{BackupSlug}-restore-{instance.VmId}.tar";
        if (sftp.Exists(tempArchivePath))
        {
            sftp.DeleteFile(tempArchivePath);
        }

        GameServerConnectorSshOps.UploadLocalFile(sftp, archiveFilePath, tempArchivePath);
        Logger.LogInformation(
            "SSH restore archive uploaded game={GameName} vmId={VmId} host={Host} bytes={Bytes}",
            GameDisplayName,
            instance.VmId,
            host,
            archiveSizeBytes);

        var restoreScript = BuildBackupRestoreScript(instance, tempArchivePath);
        using var restoreCmd = ssh.CreateCommand(GameServerConnectorSshOps.NormalizeShellCommand(restoreScript));
        restoreCmd.CommandTimeout = TimeSpan.FromMinutes(20);
        restoreCmd.Execute();
        if (restoreCmd.ExitStatus != 0)
        {
            throw new InvalidOperationException($"Restore command failed: {restoreCmd.Error}");
        }
    }
}
