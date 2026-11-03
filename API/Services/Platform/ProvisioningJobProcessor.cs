using System.Text.Json;
using System.Text;
using System.Data;
using PlaymoxAPI.BLL;
using PlaymoxAPI.BLL.BO.Platform;
using PlaymoxAPI.Helpers;
using PlaymoxAPI.Services.Proxmox;
using Microsoft.EntityFrameworkCore;

namespace PlaymoxAPI.Services.Platform;

public enum JobProcessingLane
{
    Proxmox = 1,
    VmScoped = 2
}

public class ProvisioningJobProcessor
{
    private const int DefaultMaxRetries = 3;
    private static readonly TimeSpan ProxmoxTaskTimeout = TimeSpan.FromMinutes(5);
    private static readonly ProvisioningOperation[] VmScopedOperations =
    [
        ProvisioningOperation.InstallGameServer,
        ProvisioningOperation.ReinstallGameServer,
        ProvisioningOperation.UpdateGameConfiguration
    ];
    private readonly ProxmoxAutomationClient proxmoxAutomationClient;
    private readonly GameServerConnectorRegistry gameServerConnectorRegistry;

    public ProvisioningJobProcessor(
        ProxmoxAutomationClient proxmoxAutomationClient,
        GameServerConnectorRegistry gameServerConnectorRegistry)
    {
        this.proxmoxAutomationClient = proxmoxAutomationClient;
        this.gameServerConnectorRegistry = gameServerConnectorRegistry;
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
        => await ProcessNextAsync(JobProcessingLane.Proxmox, cancellationToken)
           || await ProcessNextAsync(JobProcessingLane.VmScoped, cancellationToken);

    public async Task<bool> ProcessNextAsync(JobProcessingLane lane, CancellationToken cancellationToken)
    {
        using var dbContext = new PlaymoxContext();
        var job = await ClaimNextQueuedJobAsync(dbContext, lane, cancellationToken);
        if (job == null)
        {
            return false;
        }

        try
        {
            var instance = job.VirtualGameInstanceId.HasValue
                ? await dbContext.VirtualGameInstances.FirstOrDefaultAsync(x => x.Id == job.VirtualGameInstanceId.Value, cancellationToken)
                : null;

            await ExecuteJobAsync(dbContext, job, instance, cancellationToken);

            await dbContext.Entry(job).ReloadAsync(cancellationToken);
            if (job.Status != ProvisioningJobStatus.Running)
            {
                return true;
            }

            job.Status = ProvisioningJobStatus.Succeeded;
            job.NextAttemptUtc = null;
            job.CompletedDateUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            var retryState = InstanceLifecycleService.ParseRetryState(job.ResultJson);
            var nextRetry = retryState.RetryCount + 1;
            var maxRetries = retryState.MaxRetries > 0 ? retryState.MaxRetries : DefaultMaxRetries;
            var retryable = IsRetryable(ex);
            job.ErrorMessage = ex.Message;
            job.ResultJson = InstanceLifecycleService.BuildResultJson(nextRetry, maxRetries);

            if (retryable && nextRetry < maxRetries)
            {
                job.Status = ProvisioningJobStatus.Queued;
                job.NextAttemptUtc = DateTime.UtcNow.Add(CalculateRetryBackoff(nextRetry));
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            }

            job.Status = ProvisioningJobStatus.Failed;
            job.NextAttemptUtc = null;
            job.CompletedDateUtc = DateTime.UtcNow;
            if (job.VirtualGameInstanceId.HasValue)
            {
                var failedInstance = await dbContext.VirtualGameInstances
                    .FirstOrDefaultAsync(x => x.Id == job.VirtualGameInstanceId.Value, cancellationToken);
                if (failedInstance != null &&
                    (failedInstance.Status == VirtualInstanceStatus.Creating || failedInstance.Status == VirtualInstanceStatus.Updating))
                {
                    failedInstance.Status = VirtualInstanceStatus.Error;
                }
            }

            if (job.Operation == ProvisioningOperation.CreateVm && job.VirtualGameInstanceId.HasValue)
            {
                await TryCompensateFailedCreateVmAsync(dbContext, job.VirtualGameInstanceId.Value, cancellationToken);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
    }

    private static async Task<ProvisioningJob?> ClaimNextQueuedJobAsync(
        PlaymoxContext dbContext,
        JobProcessingLane lane,
        CancellationToken cancellationToken)
    {
        var allowedOperations = lane == JobProcessingLane.VmScoped
            ? VmScopedOperations
            : Enum.GetValues<ProvisioningOperation>().Except(VmScopedOperations).ToArray();

        const int maxClaimAttempts = 3;
        for (var claimAttempt = 0; claimAttempt < maxClaimAttempts; claimAttempt++)
        {
            try
            {
                await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var nowUtc = DateTime.UtcNow;
                var candidates = await dbContext.ProvisioningJobs
                    .AsNoTracking()
                    .Where(x => x.Status == ProvisioningJobStatus.Queued &&
                                allowedOperations.Contains(x.Operation) &&
                                (!x.NextAttemptUtc.HasValue || x.NextAttemptUtc <= nowUtc))
                    .OrderBy(x => x.Id)
                    .Take(200)
                    .ToListAsync(cancellationToken);

                foreach (var candidate in candidates)
                {
                    if (candidate.VirtualGameInstanceId.HasValue)
                    {
                        var hasRunningOnVm = await dbContext.ProvisioningJobs.AsNoTracking()
                            .AnyAsync(
                                x => x.Status == ProvisioningJobStatus.Running &&
                                     x.VirtualGameInstanceId == candidate.VirtualGameInstanceId &&
                                     x.Id != candidate.Id,
                                cancellationToken);
                        if (hasRunningOnVm)
                        {
                            continue;
                        }
                    }

                    var started = DateTime.UtcNow;
                    var updatedRows = await dbContext.ProvisioningJobs
                        .Where(x => x.Id == candidate.Id &&
                                    x.Status == ProvisioningJobStatus.Queued &&
                                    (!x.NextAttemptUtc.HasValue || x.NextAttemptUtc <= started))
                        .ExecuteUpdateAsync(setters => setters
                                .SetProperty(x => x.Status, ProvisioningJobStatus.Running)
                                .SetProperty(x => x.ErrorMessage, (string?)null)
                                .SetProperty(x => x.NextAttemptUtc, (DateTime?)null)
                                .SetProperty(x => x.StartedDateUtc, started),
                            cancellationToken);
                    if (updatedRows == 0)
                    {
                        continue;
                    }

                    var claimed = await dbContext.ProvisioningJobs.FirstOrDefaultAsync(x => x.Id == candidate.Id, cancellationToken);
                    await tx.CommitAsync(cancellationToken);
                    return claimed;
                }

                await tx.CommitAsync(cancellationToken);
                return null;
            }
            catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 1205 })
            {
                if (claimAttempt == maxClaimAttempts - 1)
                {
                    throw;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(150 * (claimAttempt + 1)), cancellationToken);
            }
        }

        return null;
    }

    private async Task ExecuteJobAsync(
        PlaymoxContext dbContext,
        ProvisioningJob job,
        VirtualGameInstance? instance,
        CancellationToken cancellationToken)
    {
        var payload = InstanceLifecycleService.ParsePayload(job.PayloadJson);
        switch (job.Operation)
        {
            case ProvisioningOperation.CreateVm:
                if (instance == null)
                {
                    throw new InvalidOperationException("CreateVm job is missing target instance.");
                }
                await ExecuteCreateVmAsync(dbContext, job, instance, payload, cancellationToken);
                instance.Status = VirtualInstanceStatus.Running;
                break;

            case ProvisioningOperation.StartVm:
                if (instance == null)
                {
                    throw new InvalidOperationException("StartVm job is missing target instance.");
                }
                await EnsureManagedPlaymoxVmAsync(instance.VmId, cancellationToken);
                await ExecuteWithTaskWaitAsync(
                    await ExecuteWithVmLockRetryAsync(
                        () => proxmoxAutomationClient.SetVmAutostartAsync(instance.VmId, true, cancellationToken),
                        cancellationToken),
                    cancellationToken);
                await ExecuteWithTaskWaitAsync(
                    await ExecuteWithVmLockRetryAsync(
                        () => proxmoxAutomationClient.StartVmAsync(instance.VmId, cancellationToken),
                        cancellationToken),
                    cancellationToken);
                instance.Status = VirtualInstanceStatus.Running;
                break;

            case ProvisioningOperation.StopVm:
                if (instance == null)
                {
                    throw new InvalidOperationException("StopVm job is missing target instance.");
                }
                await EnsureManagedPlaymoxVmAsync(instance.VmId, cancellationToken);
                await ExecuteWithTaskWaitAsync(
                    await ExecuteWithVmLockRetryAsync(
                        () => proxmoxAutomationClient.SetVmAutostartAsync(instance.VmId, false, cancellationToken),
                        cancellationToken),
                    cancellationToken);
                await ExecuteWithTaskWaitAsync(
                    await ExecuteWithVmLockRetryAsync(
                        () => proxmoxAutomationClient.StopVmAsync(instance.VmId, cancellationToken),
                        cancellationToken),
                    cancellationToken);
                instance.Status = VirtualInstanceStatus.Stopped;
                break;

            case ProvisioningOperation.ShutdownVm:
                if (instance == null)
                {
                    throw new InvalidOperationException("ShutdownVm job is missing target instance.");
                }
                await EnsureManagedPlaymoxVmAsync(instance.VmId, cancellationToken);
                await StopGameServerGracefullyAsync(dbContext, instance, cancellationToken);
                await ExecuteWithTaskWaitAsync(
                    await ExecuteWithVmLockRetryAsync(
                        () => proxmoxAutomationClient.SetVmAutostartAsync(instance.VmId, false, cancellationToken),
                        cancellationToken),
                    cancellationToken);
                await ExecuteWithTaskWaitAsync(
                    await ExecuteWithVmLockRetryAsync(
                        () => proxmoxAutomationClient.ShutdownVmAsync(instance.VmId, cancellationToken),
                        cancellationToken),
                    cancellationToken);
                instance.Status = VirtualInstanceStatus.Stopped;
                break;

            case ProvisioningOperation.RestartVm:
                if (instance == null)
                {
                    throw new InvalidOperationException("RestartVm job is missing target instance.");
                }
                await EnsureManagedPlaymoxVmAsync(instance.VmId, cancellationToken);
                await ExecuteWithTaskWaitAsync(
                    await ExecuteWithVmLockRetryAsync(
                        () => proxmoxAutomationClient.SetVmAutostartAsync(instance.VmId, true, cancellationToken),
                        cancellationToken),
                    cancellationToken);
                await ExecuteWithTaskWaitAsync(
                    await ExecuteWithVmLockRetryAsync(
                        () => proxmoxAutomationClient.RestartVmAsync(instance.VmId, cancellationToken),
                        cancellationToken),
                    cancellationToken);
                instance.Status = VirtualInstanceStatus.Running;
                break;

            case ProvisioningOperation.DeleteVm:
                if (instance == null)
                {
                    throw new InvalidOperationException("DeleteVm job is missing target instance.");
                }

                var vmExists = await proxmoxAutomationClient.VmExistsAsync(instance.VmId, cancellationToken);
                if (vmExists)
                {
                    var isManaged = await proxmoxAutomationClient.IsManagedPlaymoxVmAsync(instance.VmId, cancellationToken);
                    if (!isManaged)
                    {
                        throw new InvalidOperationException(
                            $"Safety check failed: Proxmox VM {instance.VmId} exists but is NOT managed by Playmox. Deletion aborted to protect unmanaged resources.");
                    }

                    await TryStopBeforeDeleteAsync(instance.VmId, cancellationToken);
                    await ExecuteWithTaskWaitAsync(await proxmoxAutomationClient.DeleteVmAsync(instance.VmId, cancellationToken), cancellationToken);
                }

                instance.Status = VirtualInstanceStatus.Deleted;
                await ReleasePortLeasesAsync(dbContext, instance.Id, cancellationToken);
                break;

            case ProvisioningOperation.UpdateVmResources:
                if (instance == null)
                {
                    throw new InvalidOperationException("UpdateVmResources job is missing target instance.");
                }
                if (!payload.CpuCores.HasValue || !payload.MemoryMb.HasValue)
                {
                    throw new InvalidOperationException("UpdateVmResources job payload is invalid.");
                }
                await EnsureManagedPlaymoxVmAsync(instance.VmId, cancellationToken);
                instance.Status = VirtualInstanceStatus.Updating;
                await dbContext.SaveChangesAsync(cancellationToken);

                await ExecuteWithTaskWaitAsync(
                    await ExecuteWithVmLockRetryAsync(
                        () => proxmoxAutomationClient.SetVmResourcesAsync(instance.VmId, payload.CpuCores.Value, payload.MemoryMb.Value, cancellationToken),
                        cancellationToken),
                    cancellationToken);

                if (payload.DiskGb.HasValue)
                {
                    var currentDisk = await proxmoxAutomationClient.GetVmPrimaryDiskSizeGbAsync(instance.VmId, cancellationToken)
                                      ?? instance.DiskGb.GetValueOrDefault();
                    var delta = payload.DiskGb.Value - currentDisk;
                    if (delta > 0)
                    {
                        await ExecuteWithTaskWaitAsync(
                            await ExecuteWithVmLockRetryAsync(
                                () => proxmoxAutomationClient.ResizeVmDiskAsync(instance.VmId, delta, cancellationToken),
                                cancellationToken),
                            cancellationToken);
                    }
                }

                instance.CpuCores = payload.CpuCores.Value;
                instance.MemoryMb = payload.MemoryMb.Value;
                instance.DiskGb = payload.DiskGb;
                instance.VmDisplayName = string.IsNullOrWhiteSpace(payload.RequestedVmName)
                    ? null
                    : payload.RequestedVmName.Trim();
                await RestartGameAfterVmResourceChangeAsync(dbContext, instance, cancellationToken);
                var vmStatus = await proxmoxAutomationClient.GetVmStatusAsync(instance.VmId, cancellationToken);
                instance.Status = NormalizeVmStatus(vmStatus);
                break;

            case ProvisioningOperation.ResetVmPassword:
                if (instance == null)
                {
                    throw new InvalidOperationException("ResetVmPassword job is missing target instance.");
                }
                if (string.IsNullOrWhiteSpace(payload.AccessUsername) || string.IsNullOrWhiteSpace(payload.AccessPasswordEncrypted))
                {
                    throw new InvalidOperationException("ResetVmPassword job payload is invalid.");
                }
                await EnsureManagedPlaymoxVmAsync(instance.VmId, cancellationToken);

                var newPassword = SecureSecret.Decrypt(payload.AccessPasswordEncrypted);
                string? resetPasswordTask;
                try
                {
                    resetPasswordTask = await proxmoxAutomationClient.SetVmGuestUserPasswordAsync(
                        instance.VmId,
                        payload.AccessUsername,
                        newPassword,
                        cancellationToken);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("VM.GuestAgent.Unrestricted", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Password reset requires Proxmox privilege VM.GuestAgent.Unrestricted for the API token. Rerun Proxmox bootstrap to refresh role/ACL.",
                        ex);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("QEMU guest agent is not running", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Password reset failed because QEMU guest agent is not running inside VM. Recreate template with updated bootstrap (auto-installs qemu-guest-agent) and create a new VM.",
                        ex);
                }
                await ExecuteWithTaskWaitAsync(resetPasswordTask, cancellationToken);

                var currentUserConfigJson = InstanceLifecycleService.ExtractUserConfigurationJson(instance.ConfigurationJson);
                instance.ConfigurationJson = BuildMergedConfigurationJson(
                    currentUserConfigJson,
                    payload.AccessUsername,
                    payload.AccessPasswordEncrypted,
                    ExtractCurrentSteamUsername(instance.ConfigurationJson),
                    ExtractCurrentSteamEncryptedPassword(instance.ConfigurationJson));
                break;

            case ProvisioningOperation.InstallGameServer:
            case ProvisioningOperation.ReinstallGameServer:
                if (instance == null)
                {
                    throw new InvalidOperationException($"{job.Operation} job is missing target instance.");
                }
                await ExecuteGameInstallOrReinstallAsync(
                    dbContext,
                    instance,
                    payload,
                    forceReinstall: job.Operation == ProvisioningOperation.ReinstallGameServer,
                    cancellationToken);
                break;

            case ProvisioningOperation.UpdateGameConfiguration:
                if (instance == null)
                {
                    throw new InvalidOperationException("UpdateGameConfiguration job is missing target instance.");
                }

                if (string.IsNullOrWhiteSpace(payload.ConfigurationJson))
                {
                    throw new InvalidOperationException("UpdateGameConfiguration payload is invalid.");
                }

                var updatedConfiguration = BuildMergedConfigurationJson(
                    payload.ConfigurationJson,
                    ExtractCloudInitCredentials(instance.ConfigurationJson).Username,
                    ExtractCurrentEncryptedPassword(instance.ConfigurationJson),
                    ExtractCurrentSteamUsername(instance.ConfigurationJson),
                    ExtractCurrentSteamEncryptedPassword(instance.ConfigurationJson));
                instance.ConfigurationJson = updatedConfiguration;
                await ExecuteGameInstallOrReinstallAsync(
                    dbContext,
                    instance,
                    payload,
                    forceReinstall: false,
                    cancellationToken);
                await RestartGameAfterVmResourceChangeAsync(dbContext, instance, cancellationToken);
                break;

            default:
                throw new InvalidOperationException($"Unsupported provisioning operation: {job.Operation}");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ExecuteCreateVmAsync(
        PlaymoxContext dbContext,
        ProvisioningJob job,
        VirtualGameInstance instance,
        JobPayload payload,
        CancellationToken cancellationToken)
    {
        string? cloneTask = null;
        var vmAlreadyExists = await proxmoxAutomationClient.VmExistsAsync(instance.VmId, cancellationToken);
        if (!vmAlreadyExists)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    cloneTask = await proxmoxAutomationClient.CloneFromTemplateAsync(
                        instance.VmId,
                        payload.RequestedVmName ?? $"playmox-{instance.VmId}",
                        cancellationToken);
                    await ExecuteWithTaskWaitAsync(cloneTask, cancellationToken);
                    break;
                }
                catch (Exception ex) when (IsVmIdAlreadyUsedError(ex))
                {
                    if (attempt == 4)
                    {
                        throw;
                    }

                    instance.VmId = await GetNextFreeVmIdAsync(instance.Id, cancellationToken);
                }
            }
        }

        var configTask = await ExecuteWithVmLockRetryAsync(
            () => proxmoxAutomationClient.SetVmResourcesAsync(
                instance.VmId,
                payload.CpuCores ?? instance.CpuCores,
                payload.MemoryMb ?? instance.MemoryMb,
                cancellationToken),
            cancellationToken);
        await ExecuteWithTaskWaitAsync(configTask, cancellationToken);

        var netTask = await ExecuteWithVmLockRetryAsync(
            () => proxmoxAutomationClient.EnsureVmNetworkAsync(instance.VmId, cancellationToken),
            cancellationToken);
        await ExecuteWithTaskWaitAsync(netTask, cancellationToken);

        var (cloudInitUser, cloudInitPassword) = ExtractCloudInitCredentials(instance.ConfigurationJson);
        var cloudInitTask = await ExecuteWithVmLockRetryAsync(
            () => proxmoxAutomationClient.SetVmCloudInitAsync(
                instance.VmId,
                cloudInitUser,
                cloudInitPassword,
                cancellationToken),
            cancellationToken);
        await ExecuteWithTaskWaitAsync(cloudInitTask, cancellationToken);

        var cloudInitRegenerateTask = await ExecuteWithVmLockRetryAsync(
            () => proxmoxAutomationClient.RegenerateCloudInitAsync(instance.VmId, cancellationToken),
            cancellationToken);
        await ExecuteWithTaskWaitAsync(cloudInitRegenerateTask, cancellationToken);

        try
        {
            var consoleTask = await ExecuteWithVmLockRetryAsync(
                () => proxmoxAutomationClient.SetVmConsoleModeAsync(instance.VmId, cancellationToken),
                cancellationToken);
            await ExecuteWithTaskWaitAsync(consoleTask, cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("Permission check failed", StringComparison.OrdinalIgnoreCase))
        {
            // Some Proxmox tokens don't have VM.Config.HWType; keep VM creation successful without this cosmetic step.
        }

        if (payload.DiskGb.HasValue && instance.DiskGb.HasValue)
        {
            var currentDisk = await proxmoxAutomationClient.GetVmPrimaryDiskSizeGbAsync(instance.VmId, cancellationToken)
                              ?? instance.DiskGb.Value;
            var delta = payload.DiskGb.Value - currentDisk;
            if (delta > 0)
            {
                var resizeTask = await ExecuteWithVmLockRetryAsync(
                    () => proxmoxAutomationClient.ResizeVmDiskAsync(instance.VmId, delta, cancellationToken),
                    cancellationToken);
                await ExecuteWithTaskWaitAsync(resizeTask, cancellationToken);
            }
        }

        var autostartTask = await ExecuteWithVmLockRetryAsync(
            () => proxmoxAutomationClient.SetVmAutostartAsync(instance.VmId, true, cancellationToken),
            cancellationToken);
        await ExecuteWithTaskWaitAsync(autostartTask, cancellationToken);

        var dnatTask = await ExecuteWithVmLockRetryAsync(
            () => proxmoxAutomationClient.ConfigureVmDnatAsync(
                instance.VmId,
                instance.SshPublicPort,
                instance.GamePublicPortStart,
                instance.GamePublicPortCount,
                instance.GamePublicPortProtocol,
                cancellationToken),
            cancellationToken);
        await ExecuteWithTaskWaitAsync(dnatTask, cancellationToken);

        var startTask = await ExecuteWithVmLockRetryAsync(
            () => proxmoxAutomationClient.StartVmAsync(instance.VmId, cancellationToken),
            cancellationToken);
        await ExecuteWithTaskWaitAsync(startTask, cancellationToken);

        await proxmoxAutomationClient.WaitForGuestAgentAsync(instance.VmId, TimeSpan.FromMinutes(5), cancellationToken);
        var guestIpv4 = await EnsureGuestIpv4Async(instance.VmId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(guestIpv4))
        {
            instance.InternalIp = guestIpv4;
        }

        var gameDefinition = await dbContext.GameDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == instance.GameDefinitionId, cancellationToken);
        if (gameDefinition == null)
        {
            throw new InvalidOperationException("Game definition does not exist for created VM.");
        }

        await ConfigureGuestNetworkingAndSshAsync(instance, cancellationToken);
        await proxmoxAutomationClient.SyncVmFirewallRulesAsync(
            instance.VmId,
            instance.SshPublicPort.HasValue ? 22 : null,
            instance.GamePublicPortStart,
            instance.GamePublicPortCount,
            instance.GamePublicPortProtocol,
            cancellationToken);
        QueueInstallGameServerJobIfNeeded(dbContext, job, instance, payload, gameDefinition);

        job.ResultJson = InstanceLifecycleService.BuildResultJson(0, DefaultMaxRetries, startTask ?? cloneTask);
    }

    private async Task ConfigureGuestNetworkingAndSshAsync(VirtualGameInstance instance, CancellationToken cancellationToken)
    {
        var sshPort = instance.SshPublicPort;
        var gameStart = instance.GamePublicPortStart;
        var gameCount = instance.GamePublicPortCount.GetValueOrDefault();
        var gameProtocols = ParseGameProtocols(instance.GamePublicPortProtocol);

        var scriptBuilder = new StringBuilder();
        scriptBuilder.AppendLine("set -e");
        scriptBuilder.AppendLine("# PLAYMOX_SUMMARY: Configure VM networking, firewall and SSH");
        scriptBuilder.AppendLine("export DEBIAN_FRONTEND=noninteractive");
        scriptBuilder.AppendLine("if [ ! -f /var/lib/apt/periodic/update-success-stamp ] || [ -n \"$(find /var/lib/apt/periodic/update-success-stamp -mmin +60 -print -quit 2>/dev/null)\" ]; then");
        scriptBuilder.AppendLine("  apt-get -o DPkg::Lock::Timeout=300 update -y >/dev/null");
        scriptBuilder.AppendLine("fi");
        scriptBuilder.AppendLine("apt-get -o DPkg::Lock::Timeout=300 install -y ufw >/dev/null");
        scriptBuilder.AppendLine("IFACE=\"$(ls /sys/class/net | grep -E '^(en|eth)' | head -n1 || true)\"");
        scriptBuilder.AppendLine("if [ -n \"$IFACE\" ]; then");
        scriptBuilder.AppendLine("  mkdir -p /etc/netplan");
        scriptBuilder.AppendLine("  cat >/etc/netplan/60-playmox-dhcp.yaml <<EOF");
        scriptBuilder.AppendLine("network:");
        scriptBuilder.AppendLine("  version: 2");
        scriptBuilder.AppendLine("  ethernets:");
        scriptBuilder.AppendLine("    ${IFACE}:");
        scriptBuilder.AppendLine("      dhcp4: true");
        scriptBuilder.AppendLine("      dhcp-identifier: mac");
        scriptBuilder.AppendLine("      dhcp4-overrides:");
        scriptBuilder.AppendLine("        use-dns: false");
        scriptBuilder.AppendLine("      nameservers:");
        scriptBuilder.AppendLine("        addresses: [8.8.8.8, 8.8.4.4]");
        scriptBuilder.AppendLine("EOF");
        scriptBuilder.AppendLine("  netplan generate || true");
        scriptBuilder.AppendLine("  netplan apply || true");
        scriptBuilder.AppendLine("  if command -v dhclient >/dev/null 2>&1; then");
        scriptBuilder.AppendLine("    dhclient -r \"$IFACE\" || true");
        scriptBuilder.AppendLine("    dhclient \"$IFACE\" || true");
        scriptBuilder.AppendLine("  fi");
        scriptBuilder.AppendLine("fi");
        scriptBuilder.AppendLine("ufw --force reset");
        scriptBuilder.AppendLine("ufw default deny incoming");
        scriptBuilder.AppendLine("ufw default allow outgoing");

        scriptBuilder.AppendLine("ufw allow 22/tcp");
        scriptBuilder.AppendLine("mkdir -p /etc/ssh/sshd_config.d");
        scriptBuilder.AppendLine("rm -f /etc/ssh/sshd_config.d/90-playmox-password-auth.conf || true");
        scriptBuilder.AppendLine("cat >/etc/ssh/sshd_config.d/99-playmox-password-auth.conf <<'EOF'");
        scriptBuilder.AppendLine("PasswordAuthentication yes");
        scriptBuilder.AppendLine("KbdInteractiveAuthentication yes");
        scriptBuilder.AppendLine("ChallengeResponseAuthentication yes");
        scriptBuilder.AppendLine("UsePAM yes");
        scriptBuilder.AppendLine("EOF");
        scriptBuilder.AppendLine("chmod 644 /etc/ssh/sshd_config.d/99-playmox-password-auth.conf");
        scriptBuilder.AppendLine("systemctl enable --now ssh || systemctl enable --now sshd");
        scriptBuilder.AppendLine("sshd -t || /usr/sbin/sshd -t");
        scriptBuilder.AppendLine("systemctl restart ssh || systemctl restart sshd");

        if (gameStart.HasValue && gameCount > 0)
        {
            for (var offset = 0; offset < gameCount; offset++)
            {
                var port = gameStart.Value + offset;
                foreach (var protocol in gameProtocols)
                {
                    scriptBuilder.AppendLine($"ufw allow {port}/{protocol}");
                }
            }
        }

        scriptBuilder.AppendLine("ufw --force enable");
        await proxmoxAutomationClient.RunGuestScriptAsync(
            instance.VmId,
            scriptBuilder.ToString(),
            TimeSpan.FromMinutes(10),
            cancellationToken);
    }

    private async Task InstallGameIfRequiredAsync(
        VirtualGameInstance instance,
        JobPayload payload,
        GameDefinition gameDefinition,
        CancellationToken cancellationToken)
    {
        var connector = gameServerConnectorRegistry.Resolve(gameDefinition);
        if (connector == null)
        {
            throw new InvalidOperationException($"Game provisioning is not implemented for: {gameDefinition.Key}");
        }

        await connector.InstallOrUpdateAsync(instance, payload.ConfigurationJson, gameDefinition, forceReinstall: false, cancellationToken);
    }

    private void QueueInstallGameServerJobIfNeeded(
        PlaymoxContext dbContext,
        ProvisioningJob createJob,
        VirtualGameInstance instance,
        JobPayload payload,
        GameDefinition gameDefinition)
    {
        var connector = gameServerConnectorRegistry.Resolve(gameDefinition);
        if (connector == null || connector.Kind == GameServerKind.BlankVm)
        {
            return;
        }

        var alreadyQueued = dbContext.ProvisioningJobs.Any(x =>
            x.VirtualGameInstanceId == instance.Id &&
            x.Operation == ProvisioningOperation.InstallGameServer &&
            (x.Status == ProvisioningJobStatus.Queued ||
             x.Status == ProvisioningJobStatus.Running ||
             x.Status == ProvisioningJobStatus.Succeeded));
        if (alreadyQueued)
        {
            return;
        }

        dbContext.ProvisioningJobs.Add(new ProvisioningJob
        {
            RequestedByUserId = createJob.RequestedByUserId,
            VirtualGameInstanceId = instance.Id,
            Operation = ProvisioningOperation.InstallGameServer,
            Status = ProvisioningJobStatus.Queued,
            PayloadJson = InstanceLifecycleService.CreatePayloadJson(new JobPayload
            {
                IdempotencyKey = payload.IdempotencyKey,
                ConfigurationJson = payload.ConfigurationJson,
                GameKey = payload.GameKey
            }),
            ResultJson = InstanceLifecycleService.BuildResultJson(0, DefaultMaxRetries)
        });
    }

    private async Task ExecuteGameInstallOrReinstallAsync(
        PlaymoxContext dbContext,
        VirtualGameInstance instance,
        JobPayload payload,
        bool forceReinstall,
        CancellationToken cancellationToken)
    {
        var gameDefinition = await dbContext.GameDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == instance.GameDefinitionId, cancellationToken);
        if (gameDefinition == null)
        {
            throw new InvalidOperationException("Game definition does not exist for VM.");
        }

        var connector = gameServerConnectorRegistry.Resolve(gameDefinition);
        if (connector == null)
        {
            throw new InvalidOperationException($"Game provisioning is not implemented for: {gameDefinition.Key}");
        }

        var expectedGameProtocol = ResolveGamePortProtocol(gameDefinition.Key);
        if (!string.Equals(instance.GamePublicPortProtocol, expectedGameProtocol, StringComparison.OrdinalIgnoreCase))
        {
            instance.GamePublicPortProtocol = expectedGameProtocol;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        await ConfigureGuestNetworkingAndSshAsync(instance, cancellationToken);
        await proxmoxAutomationClient.SyncVmFirewallRulesAsync(
            instance.VmId,
            instance.SshPublicPort.HasValue ? 22 : null,
            instance.GamePublicPortStart,
            instance.GamePublicPortCount,
            instance.GamePublicPortProtocol,
            cancellationToken);

        var configJson = !string.IsNullOrWhiteSpace(payload.ConfigurationJson)
            ? payload.ConfigurationJson
            : InstanceLifecycleService.ExtractUserConfigurationJson(instance.ConfigurationJson);
        await connector.InstallOrUpdateAsync(
            instance,
            configJson,
            gameDefinition,
            forceReinstall,
            cancellationToken);
    }

    private async Task StopGameServerGracefullyAsync(
        PlaymoxContext dbContext,
        VirtualGameInstance instance,
        CancellationToken cancellationToken)
    {
        var gameDefinition = await dbContext.GameDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == instance.GameDefinitionId, cancellationToken);
        if (gameDefinition == null)
        {
            return;
        }

        var connector = gameServerConnectorRegistry.Resolve(gameDefinition);
        if (connector == null)
        {
            return;
        }

        await connector.StopForShutdownAsync(instance, cancellationToken);
    }

    private async Task RestartGameAfterVmResourceChangeAsync(
        PlaymoxContext dbContext,
        VirtualGameInstance instance,
        CancellationToken cancellationToken)
    {
        var vmStatus = await proxmoxAutomationClient.GetVmStatusAsync(instance.VmId, cancellationToken);
        if (!string.Equals(vmStatus, "running", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var gameDefinition = await dbContext.GameDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == instance.GameDefinitionId, cancellationToken);
        if (gameDefinition == null)
        {
            return;
        }

        var connector = gameServerConnectorRegistry.Resolve(gameDefinition);
        if (connector == null)
        {
            return;
        }

        await connector.RestartAfterVmResourceChangeAsync(instance, cancellationToken);
    }

    private static string ResolveGamePortProtocol(string gameKey)
        => string.Equals(gameKey, "terraria", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(gameKey, "unturned", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(gameKey, "factorio", StringComparison.OrdinalIgnoreCase)
            ? "udp,tcp"
            : "tcp";

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

    private async Task<int> GetNextFreeVmIdAsync(long currentInstanceId, CancellationToken cancellationToken)
    {
        var candidate = await proxmoxAutomationClient.GetNextVmIdAsync(cancellationToken);
        while (true)
        {
            using var dbContext = new PlaymoxContext();
            var usedInDb = await dbContext.VirtualGameInstances.AsNoTracking()
                .AnyAsync(x => x.Id != currentInstanceId && x.VmId == candidate, cancellationToken);
            if (usedInDb || await proxmoxAutomationClient.VmExistsAsync(candidate, cancellationToken))
            {
                candidate++;
                continue;
            }

            return candidate;
        }
    }

    private static bool IsVmIdAlreadyUsedError(Exception ex)
    {
        var message = ex.Message;
        return message.Contains("config file already exists", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unable to create VM", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRetryable(Exception ex)
    {
        var message = ex.Message;
        if (message.Contains("Permission check failed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (message.Contains("guest agent", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("QEMU agent", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not running", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("command not found", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static TimeSpan CalculateRetryBackoff(int retryAttempt)
    {
        var exponent = Math.Clamp(retryAttempt - 1, 0, 6);
        var baseDelaySeconds = 5 * (1 << exponent);
        var cappedBaseDelay = TimeSpan.FromSeconds(Math.Min(baseDelaySeconds, 300));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1200));
        var delay = cappedBaseDelay + jitter;
        return delay > TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : delay;
    }

    private async Task TryCompensateFailedCreateVmAsync(
        PlaymoxContext dbContext,
        long instanceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var instance = await dbContext.VirtualGameInstances
                .FirstOrDefaultAsync(x => x.Id == instanceId, cancellationToken);
            if (instance == null)
            {
                return;
            }

            var vmExists = await proxmoxAutomationClient.VmExistsAsync(instance.VmId, cancellationToken);
            if (!vmExists)
            {
                await ReleasePortLeasesAsync(dbContext, instance.Id, cancellationToken);
                return;
            }

            var isManagedVm = await proxmoxAutomationClient.IsManagedPlaymoxVmAsync(instance.VmId, cancellationToken);
            if (!isManagedVm)
            {
                return;
            }

            await TryStopBeforeDeleteAsync(instance.VmId, cancellationToken);
            await ExecuteWithTaskWaitAsync(await proxmoxAutomationClient.DeleteVmAsync(instance.VmId, cancellationToken), cancellationToken);
            await ReleasePortLeasesAsync(dbContext, instance.Id, cancellationToken);
        }
        catch
        {
            // Preserve original failure as primary error; compensation is best-effort.
        }
    }

    private async Task EnsureManagedPlaymoxVmAsync(int vmId, CancellationToken cancellationToken)
    {
        var vmExists = await proxmoxAutomationClient.VmExistsAsync(vmId, cancellationToken);
        if (!vmExists)
        {
            return;
        }

        var isManaged = await proxmoxAutomationClient.IsManagedPlaymoxVmAsync(vmId, cancellationToken);
        if (!isManaged)
        {
            throw new InvalidOperationException(
                $"Safety check failed: Proxmox VM {vmId} exists but is NOT managed by Playmox (missing 'playmox-' name prefix or description tag). Action aborted to protect unmanaged resources.");
        }
    }

    private static async Task ReleasePortLeasesAsync(
        PlaymoxContext dbContext,
        long instanceId,
        CancellationToken cancellationToken)
    {
        await dbContext.PortLeases
            .Where(x => x.VirtualGameInstanceId == instanceId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static (string Username, string Password) ExtractCloudInitCredentials(string configurationJson)
    {
        try
        {
            using var json = JsonDocument.Parse(configurationJson);
            if (!json.RootElement.TryGetProperty("systemAccess", out var access))
            {
                throw new InvalidOperationException("Missing or invalid systemAccess credentials in VM configuration.");
            }

            var username = access.TryGetProperty("username", out var usernameProp) && usernameProp.ValueKind == JsonValueKind.String
                ? usernameProp.GetString()
                : null;
            var encrypted = access.TryGetProperty("passwordEncrypted", out var passProp) && passProp.ValueKind == JsonValueKind.String
                ? passProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(encrypted))
            {
                throw new InvalidOperationException("Missing or invalid systemAccess credentials in VM configuration.");
            }

            return (username!, SecureSecret.Decrypt(encrypted));
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException("Missing or invalid systemAccess credentials in VM configuration.", ex);
        }
    }

    private static string ExtractCurrentEncryptedPassword(string configurationJson)
    {
        try
        {
            using var json = JsonDocument.Parse(configurationJson);
            if (json.RootElement.TryGetProperty("systemAccess", out var access) &&
                access.TryGetProperty("passwordEncrypted", out var passProp) &&
                passProp.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(passProp.GetString()))
            {
                return passProp.GetString()!;
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException("Missing or invalid systemAccess credentials in VM configuration.", ex);
        }

        throw new InvalidOperationException("Missing or invalid systemAccess credentials in VM configuration.");
    }

    private static string BuildMergedConfigurationJson(
        string? inputJson,
        string username,
        string encryptedPassword,
        string? steamUsername = null,
        string? steamPasswordEncrypted = null)
    {
        JsonElement userConfig;
        if (!string.IsNullOrWhiteSpace(inputJson))
        {
            try
            {
                using var inputDoc = JsonDocument.Parse(inputJson);
                userConfig = inputDoc.RootElement.Clone();
            }
            catch
            {
                userConfig = JsonSerializer.SerializeToElement(new { raw = inputJson });
            }
        }
        else
        {
            userConfig = JsonSerializer.SerializeToElement(new { });
        }

        var root = new Dictionary<string, object?>
        {
            ["user"] = JsonSerializer.Deserialize<object>(userConfig.GetRawText()),
            ["systemAccess"] = new
            {
                username,
                passwordEncrypted = encryptedPassword
            }
        };

        if (!string.IsNullOrWhiteSpace(steamUsername) && !string.IsNullOrWhiteSpace(steamPasswordEncrypted))
        {
            root["systemSteam"] = new
            {
                username = steamUsername,
                passwordEncrypted = steamPasswordEncrypted
            };
        }

        return JsonSerializer.Serialize(root);
    }

    private static string? ExtractCurrentSteamUsername(string configurationJson)
    {
        try
        {
            using var json = JsonDocument.Parse(configurationJson);
            if (json.RootElement.TryGetProperty("systemSteam", out var steam) &&
                steam.TryGetProperty("username", out var usernameProp) &&
                usernameProp.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(usernameProp.GetString()))
            {
                return usernameProp.GetString()!;
            }
        }
        catch
        {
            // fallback below
        }

        return null;
    }

    private static string? ExtractCurrentSteamEncryptedPassword(string configurationJson)
    {
        try
        {
            using var json = JsonDocument.Parse(configurationJson);
            if (json.RootElement.TryGetProperty("systemSteam", out var steam) &&
                steam.TryGetProperty("passwordEncrypted", out var passProp) &&
                passProp.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(passProp.GetString()))
            {
                return passProp.GetString()!;
            }
        }
        catch
        {
            // fallback below
        }

        return null;
    }

    private async Task<string?> EnsureGuestIpv4Async(int vmId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var ip = await proxmoxAutomationClient.TryGetVmGuestIpv4Async(vmId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(ip))
            {
                return ip;
            }

            await TryRepairGuestDhcpAsync(vmId, cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }

        return await proxmoxAutomationClient.TryGetVmGuestIpv4Async(vmId, cancellationToken);
    }

    private async Task TryRepairGuestDhcpAsync(int vmId, CancellationToken cancellationToken)
    {
        const string script = """
set -e
IFACE="$(ls /sys/class/net | grep -E '^(en|eth)' | head -n1 || true)"
if [ -z "$IFACE" ]; then
  exit 0
fi
ip link set "$IFACE" up || true
mkdir -p /etc/netplan
cat >/etc/netplan/60-playmox-dhcp.yaml <<EOF
network:
  version: 2
  ethernets:
    ${IFACE}:
      dhcp4: true
      dhcp-identifier: mac
EOF
netplan generate || true
netplan apply || true
if command -v dhclient >/dev/null 2>&1; then
  dhclient -v "$IFACE" || true
fi
""";

        try
        {
            await proxmoxAutomationClient.RunGuestScriptAsync(vmId, script, TimeSpan.FromMinutes(2), cancellationToken);
        }
        catch
        {
            // Best-effort network self-heal before game/firewall provisioning.
        }
    }

    private static bool IsVmConfigLockTimeout(Exception ex)
    {
        var message = ex.Message;
        return message.Contains("can't lock file", StringComparison.OrdinalIgnoreCase) &&
               message.Contains("/var/lock/qemu-server/lock-", StringComparison.OrdinalIgnoreCase) &&
               message.Contains("got timeout", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> ExecuteWithVmLockRetryAsync(
        Func<Task<string?>> action,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 12;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (IsVmConfigLockTimeout(ex) && attempt < maxAttempts)
            {
                var delaySeconds = Math.Min(2 + attempt, 15);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        return await action();
    }

    private async Task ExecuteWithTaskWaitAsync(string? upid, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(upid))
        {
            return;
        }

        // Some Proxmox endpoints (e.g. guest agent actions) return data payloads
        // instead of task UPIDs. Wait only for real UPID handles.
        if (!upid.StartsWith("UPID:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await proxmoxAutomationClient.WaitForTaskAsync(upid, ProxmoxTaskTimeout, cancellationToken);
    }

    private async Task TryStopBeforeDeleteAsync(int vmId, CancellationToken cancellationToken)
    {
        try
        {
            var stopTask = await proxmoxAutomationClient.StopVmAsync(vmId, cancellationToken);
            await ExecuteWithTaskWaitAsync(stopTask, cancellationToken);
        }
        catch
        {
            // VM may already be stopped or missing; final delete call is authoritative.
        }
    }

    private static VirtualInstanceStatus NormalizeVmStatus(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "running" => VirtualInstanceStatus.Running,
            "stopped" => VirtualInstanceStatus.Stopped,
            _ => VirtualInstanceStatus.Error
        };
    }

}
