using System.Collections.Concurrent;
using System.Net;
using PlaymoxAPI.BLL;
using PlaymoxAPI.BLL.BO.Platform;
using PlaymoxAPI.Services.Proxmox;
using Microsoft.EntityFrameworkCore;

namespace PlaymoxAPI.Services.Platform;

public class InstanceStateSyncService : BackgroundService
{
    private static readonly ConcurrentDictionary<int, DateTime> StaticPinAttemptsUtc = new();
    private readonly ProxmoxAutomationClient proxmoxAutomationClient;
    private readonly ILogger<InstanceStateSyncService> logger;

    public InstanceStateSyncService(
        ProxmoxAutomationClient proxmoxAutomationClient,
        ILogger<InstanceStateSyncService> logger)
    {
        this.proxmoxAutomationClient = proxmoxAutomationClient;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncInstancesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background VM sync failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
    }

    private async Task SyncInstancesAsync(CancellationToken cancellationToken)
    {
        using var dbContext = new PlaymoxContext();
        var nowUtc = DateTime.UtcNow;
        var pendingInstanceIds = await dbContext.ProvisioningJobs.AsNoTracking()
            .Where(x => x.VirtualGameInstanceId.HasValue &&
                        (x.Status == ProvisioningJobStatus.Queued || x.Status == ProvisioningJobStatus.Running))
            .Select(x => x.VirtualGameInstanceId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var pendingSet = pendingInstanceIds.ToHashSet();

        var instances = await dbContext.VirtualGameInstances
            .Where(x => x.Status != VirtualInstanceStatus.Deleted)
            .ToListAsync(cancellationToken);

        foreach (var instance in instances)
        {
            if (pendingSet.Contains(instance.Id))
            {
                continue;
            }

            var exists = await proxmoxAutomationClient.VmExistsAsync(instance.VmId, cancellationToken);
            if (!exists)
            {
                if (instance.Status != VirtualInstanceStatus.Error)
                {
                    instance.Status = VirtualInstanceStatus.Error;
                }
                continue;
            }

            var vmStatus = await proxmoxAutomationClient.GetVmStatusAsync(instance.VmId, cancellationToken);
            var normalized = NormalizeVmStatus(vmStatus);
            if (instance.Status == VirtualInstanceStatus.Running && normalized == VirtualInstanceStatus.Stopped)
            {
                instance.Status = VirtualInstanceStatus.Stopped;
            }
            else if (normalized == VirtualInstanceStatus.Error)
            {
                instance.Status = VirtualInstanceStatus.Error;
            }
            else if (instance.Status != VirtualInstanceStatus.Creating && instance.Status != VirtualInstanceStatus.Updating)
            {
                instance.Status = normalized;
            }

            if (normalized != VirtualInstanceStatus.Running)
            {
                continue;
            }

            var guestIp = await proxmoxAutomationClient.TryGetVmGuestIpv4Async(instance.VmId, cancellationToken);
            if (IsValidGuestIpv4(guestIp))
            {
                instance.InternalIp = guestIp!.Trim();
            }

            if (instance.CreatedDate > nowUtc.AddMinutes(-4))
            {
                continue;
            }

            if (!IsValidGuestIpv4(instance.InternalIp))
            {
                continue;
            }

            var lastAttempt = StaticPinAttemptsUtc.GetValueOrDefault(instance.VmId);
            if (lastAttempt > nowUtc.AddHours(-12))
            {
                continue;
            }

            var pinned = await proxmoxAutomationClient.TryPinGuestDhcpAddressAsStaticAsync(
                instance.VmId,
                instance.InternalIp!,
                cancellationToken);
            StaticPinAttemptsUtc[instance.VmId] = nowUtc;
            if (pinned)
            {
                logger.LogInformation("Pinned DHCP IP as static vmId={VmId} ip={Ip}", instance.VmId, instance.InternalIp);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsValidGuestIpv4(string? ip)
    {
        if (!IPAddress.TryParse(ip, out var parsed) || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var value = ip!.Trim();
        return !value.StartsWith("127.", StringComparison.Ordinal) &&
               !value.StartsWith("169.254.", StringComparison.Ordinal) &&
               !value.StartsWith("0.", StringComparison.Ordinal);
    }

    private static VirtualInstanceStatus NormalizeVmStatus(string status)
    {
        return status.Trim().ToLowerInvariant() switch
        {
            "running" => VirtualInstanceStatus.Running,
            "stopped" => VirtualInstanceStatus.Stopped,
            _ => VirtualInstanceStatus.Error
        };
    }
}
