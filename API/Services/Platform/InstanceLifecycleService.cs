using System.Text.Json;
using PlaymoxAPI.BLL;
using PlaymoxAPI.BLL.BO.Platform;
using PlaymoxAPI.Helpers;
using PlaymoxAPI.Services.Proxmox;
using PlaymoxAPI.Extensions;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace PlaymoxAPI.Services.Platform;

public class InstanceLifecycleService
{
    private const int MaxRetries = 3;
    private const int PublicPortRangeStart = 22000;
    private const int PublicPortRangeEnd = 32000;
    private static readonly VirtualInstanceStatus[] QuotaComputeStatuses =
    [
        VirtualInstanceStatus.Creating,
        VirtualInstanceStatus.Running,
        VirtualInstanceStatus.Updating
    ];
    private static readonly VirtualInstanceStatus[] PortAllocationStatuses =
    [
        VirtualInstanceStatus.Creating,
        VirtualInstanceStatus.Running,
        VirtualInstanceStatus.Stopped,
        VirtualInstanceStatus.Updating,
        VirtualInstanceStatus.Error
    ];
    private static readonly VirtualInstanceStatus[] DiskQuotaStatuses =
    [
        VirtualInstanceStatus.Creating,
        VirtualInstanceStatus.Running,
        VirtualInstanceStatus.Stopped,
        VirtualInstanceStatus.Updating
    ];
    private readonly ProxmoxAutomationClient proxmoxAutomationClient;
    private readonly GameServerConnectorRegistry gameServerConnectorRegistry;

    public InstanceLifecycleService(
        ProxmoxAutomationClient proxmoxAutomationClient,
        GameServerConnectorRegistry gameServerConnectorRegistry)
    {
        this.proxmoxAutomationClient = proxmoxAutomationClient;
        this.gameServerConnectorRegistry = gameServerConnectorRegistry;
    }

    public async Task<EnqueueResult> EnqueueCreateVmAsync(long userId, CreateVmRequest request, CancellationToken cancellationToken = default)
    {
        if (request.InstanceKind is not null &&
            !string.Equals(request.InstanceKind, "VM", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only VM instances are currently supported.");
        }

        if (request.CpuCores < 1 || request.MemoryMb < 256)
        {
            throw new InvalidOperationException("CPU and memory values are too low.");
        }

        if (!request.DiskGb.HasValue)
        {
            throw new InvalidOperationException("Disk size is required.");
        }

        if (request.DiskGb.Value < 1)
        {
            throw new InvalidOperationException("Disk size must be greater than 0 GB.");
        }

        using var dbContext = new PlaymoxContext();
        await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var quota = await dbContext.UsersQuotas.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (quota == null)
        {
            throw new InvalidOperationException("User quota is not configured.");
        }

        var gameDefinition = await dbContext.GameDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.GameDefinitionId && x.IsActive, cancellationToken);
        if (gameDefinition == null)
        {
            throw new InvalidOperationException("Selected game type does not exist or is disabled.");
        }

        var owner = await dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (owner == null)
        {
            throw new InvalidOperationException("Owner user not found.");
        }

        var ownedActiveInstances = await dbContext.VirtualGameInstances.AsNoTracking()
            .Where(x => x.OwnerId == userId && QuotaComputeStatuses.Contains(x.Status))
            .ToListAsync(cancellationToken);
        var ownedInstancesForDisk = await dbContext.VirtualGameInstances.AsNoTracking()
            .Where(x => x.OwnerId == userId && DiskQuotaStatuses.Contains(x.Status))
            .ToListAsync(cancellationToken);

        var requiredGamePorts = Math.Max(gameDefinition.RequiredPublicPorts, 0);
        var gamePortProtocol = ResolveGamePortProtocol(gameDefinition.Key, requiredGamePorts);
        EnsureCreationWithinQuota(
            quota,
            ownedActiveInstances,
            ownedInstancesForDisk,
            request.CpuCores,
            request.MemoryMb,
            request.DiskGb,
            requiredGamePorts);

        var publicHost = await ResolvePublicHostAsync(dbContext, cancellationToken);
        var userConfigurationJson = string.IsNullOrWhiteSpace(request.ConfigurationJson) ? "{}" : request.ConfigurationJson;

        var payload = CreatePayloadJson(new JobPayload
        {
            IdempotencyKey = request.IdempotencyKey?.Trim(),
            CpuCores = request.CpuCores,
            MemoryMb = request.MemoryMb,
            DiskGb = request.DiskGb,
            ConfigurationJson = userConfigurationJson,
            RequestedVmName = BuildVmName(userId, gameDefinition.Key, request.DisplayName),
            GameKey = gameDefinition.Key
        });

        var accessUsername = "gameserver";
        var accessPassword = GenerateSecurePassword();
        string? steamUsername = null;
        string? steamPasswordEncrypted = null;
        if (string.Equals(gameDefinition.Key, "terraria", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(gameDefinition.Key, "unturned", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(owner.SteamUsername) && !string.IsNullOrWhiteSpace(owner.SteamPasswordEncrypted))
            {
                steamUsername = owner.SteamUsername.Trim();
                steamPasswordEncrypted = owner.SteamPasswordEncrypted;
            }
        }

        var configurationJson = BuildMergedConfigurationJson(
            userConfigurationJson,
            accessUsername,
            SecureSecret.Encrypt(accessPassword),
            steamUsername,
            steamPasswordEncrypted);

        var existing = await FindExistingIdempotentJobAsync(
            dbContext,
            userId,
            ProvisioningOperation.CreateVm,
            null,
            request.IdempotencyKey,
            cancellationToken);
        if (existing != null)
        {
            return EnqueueResult.FromExisting(existing);
        }

        VirtualGameInstance? instance = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var vmId = await GetUniqueVmIdAsync(dbContext, cancellationToken);
            instance = new VirtualGameInstance
            {
                OwnerId = userId,
                GameDefinitionId = request.GameDefinitionId,
                VmId = vmId,
                VmDisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
                InstanceKind = VirtualInstanceKind.VM,
                CpuCores = request.CpuCores,
                MemoryMb = request.MemoryMb,
                DiskGb = request.DiskGb,
                Status = VirtualInstanceStatus.Creating,
                ConfigurationJson = configurationJson,
                PublicHost = publicHost,
                PublicPort = null,
                SshPublicPort = null,
                GamePublicPortStart = null,
                GamePublicPortCount = null,
                GamePublicPortProtocol = gamePortProtocol
            };
            dbContext.VirtualGameInstances.Add(instance);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException ex) when (IsDuplicateVmIdError(ex))
            {
                dbContext.Entry(instance).State = EntityState.Detached;
                if (attempt == 4)
                {
                    throw new InvalidOperationException("Cannot reserve VMID for new instance. Try again.", ex);
                }
            }
        }

        if (instance == null || instance.Id == 0)
        {
            throw new InvalidOperationException("Cannot create VM instance record.");
        }

        await BackfillPortLeasesForActiveInstancesAsync(dbContext, cancellationToken);
        var (sshPublicPort, gamePublicPortStart, gamePublicPortCount) = await AllocateAndReservePublicPortsAsync(
            dbContext,
            instance.Id,
            requiredGamePorts,
            quota.AllowSshPublicPort,
            cancellationToken);

        instance.SshPublicPort = sshPublicPort;
        instance.GamePublicPortStart = gamePublicPortStart;
        instance.GamePublicPortCount = gamePublicPortCount;
        instance.PublicPort = gamePublicPortStart;

        var job = new ProvisioningJob
        {
            RequestedByUserId = userId,
            VirtualGameInstanceId = instance.Id,
            Operation = ProvisioningOperation.CreateVm,
            Status = ProvisioningJobStatus.Queued,
            PayloadJson = payload,
            ResultJson = BuildResultJson(0, MaxRetries)
        };
        dbContext.ProvisioningJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return new EnqueueResult
        {
            JobId = job.Id,
            InstanceId = instance.Id,
            AlreadyExisted = false,
            Status = job.Status.ToString()
        };
    }

    public async Task<EnqueueResult> EnqueueVmOperationAsync(
        long userId,
        bool isAdmin,
        long instanceId,
        ProvisioningOperation operation,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        using var dbContext = new PlaymoxContext();
        await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var instance = await GetOwnedInstanceAsync(dbContext, userId, isAdmin, instanceId, cancellationToken);
        if (instance == null)
        {
            throw new InvalidOperationException("Instance not found.");
        }

        if (instance.Status == VirtualInstanceStatus.Deleted)
        {
            throw new InvalidOperationException("Cannot operate on a deleted instance.");
        }

        await EnsureNoPendingJobsForInstanceAsync(dbContext, instance.Id, cancellationToken);

        if (operation == ProvisioningOperation.StartVm)
        {
            await EnsureStartWithinQuotaAsync(dbContext, instance, cancellationToken);
        }

        var existing = await FindExistingIdempotentJobAsync(dbContext, userId, operation, instance.Id, idempotencyKey, cancellationToken);
        if (existing != null)
        {
            return EnqueueResult.FromExisting(existing);
        }

        var job = new ProvisioningJob
        {
            RequestedByUserId = userId,
            VirtualGameInstanceId = instance.Id,
            Operation = operation,
            Status = ProvisioningJobStatus.Queued,
            PayloadJson = CreatePayloadJson(new JobPayload
            {
                IdempotencyKey = idempotencyKey?.Trim()
            }),
            ResultJson = BuildResultJson(0, MaxRetries)
        };
        dbContext.ProvisioningJobs.Add(job);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsPendingJobConstraintViolation(ex))
        {
            throw new InvalidOperationException("This VM already has a pending operation. Wait for job completion.", ex);
        }
        await tx.CommitAsync(cancellationToken);

        return new EnqueueResult
        {
            JobId = job.Id,
            InstanceId = instance.Id,
            AlreadyExisted = false,
            Status = job.Status.ToString()
        };
    }

    public async Task<ForceStopResult> ForceStopInstanceAsync(
        long userId,
        bool isAdmin,
        long instanceId,
        CancellationToken cancellationToken = default)
    {
        using var dbContext = new PlaymoxContext();
        var instance = await GetOwnedInstanceAsync(dbContext, userId, isAdmin, instanceId, cancellationToken);
        if (instance == null)
        {
            throw new InvalidOperationException("Instance not found.");
        }

        if (instance.Status == VirtualInstanceStatus.Deleted)
        {
            throw new InvalidOperationException("Cannot stop a deleted instance.");
        }

        try
        {
            var autostartTask = await proxmoxAutomationClient.SetVmAutostartAsync(instance.VmId, false, cancellationToken);
            if (!string.IsNullOrWhiteSpace(autostartTask))
            {
                await proxmoxAutomationClient.WaitForTaskAsync(autostartTask, TimeSpan.FromMinutes(2), cancellationToken);
            }

            var stopTask = await proxmoxAutomationClient.StopVmAsync(instance.VmId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(stopTask))
            {
                await proxmoxAutomationClient.WaitForTaskAsync(stopTask, TimeSpan.FromMinutes(2), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Force stop failed on Proxmox: {ex.Message}", ex);
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = DateTime.UtcNow;
        var cancelledJobs = await dbContext.ProvisioningJobs
            .Where(x => x.VirtualGameInstanceId == instance.Id &&
                        (x.Status == ProvisioningJobStatus.Queued || x.Status == ProvisioningJobStatus.Running))
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, ProvisioningJobStatus.Failed)
                    .SetProperty(x => x.CompletedDateUtc, now)
                    .SetProperty(x => x.ErrorMessage, "Cancelled by user via Force Stop.")
                    .SetProperty(x => x.NextAttemptUtc, (DateTime?)null),
                cancellationToken);

        instance.Status = VirtualInstanceStatus.Stopped;
        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return new ForceStopResult
        {
            InstanceId = instance.Id,
            CancelledJobs = cancelledJobs
        };
    }

    public async Task<EnqueueResult> EnqueueResourceUpdateAsync(
        long userId,
        bool isAdmin,
        long instanceId,
        UpdateVmResourcesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.CpuCores < 1 || request.MemoryMb < 256)
        {
            throw new InvalidOperationException("CPU and memory values are too low.");
        }

        using var dbContext = new PlaymoxContext();
        await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var instance = await GetOwnedInstanceAsync(dbContext, userId, isAdmin, instanceId, cancellationToken);
        if (instance == null)
        {
            throw new InvalidOperationException("Instance not found.");
        }

        if (instance.Status == VirtualInstanceStatus.Deleted)
        {
            throw new InvalidOperationException("Cannot update a deleted instance.");
        }

        await EnsureNoPendingJobsForInstanceAsync(dbContext, instance.Id, cancellationToken);

        if (request.DiskGb.HasValue && instance.DiskGb.HasValue && request.DiskGb.Value < instance.DiskGb.Value)
        {
            throw new InvalidOperationException("Disk shrinking is not supported.");
        }

        if (request.DisplayName != null && request.DisplayName.Trim().Length > 64)
        {
            throw new InvalidOperationException("VM display name can have max 64 characters.");
        }

        var quota = await dbContext.UsersQuotas.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == instance.OwnerId, cancellationToken);
        if (quota == null)
        {
            throw new InvalidOperationException("User quota is not configured.");
        }

        var ownedActiveInstances = await dbContext.VirtualGameInstances.AsNoTracking()
            .Where(x => x.OwnerId == instance.OwnerId && QuotaComputeStatuses.Contains(x.Status))
            .ToListAsync(cancellationToken);
        var ownedInstancesForDisk = await dbContext.VirtualGameInstances.AsNoTracking()
            .Where(x => x.OwnerId == instance.OwnerId && DiskQuotaStatuses.Contains(x.Status))
            .ToListAsync(cancellationToken);

        EnsureUpdateWithinQuota(quota, ownedActiveInstances, ownedInstancesForDisk, instance, request.CpuCores, request.MemoryMb, request.DiskGb);

        var existing = await FindExistingIdempotentJobAsync(
            dbContext,
            userId,
            ProvisioningOperation.UpdateVmResources,
            instance.Id,
            request.IdempotencyKey,
            cancellationToken);
        if (existing != null)
        {
            return EnqueueResult.FromExisting(existing);
        }

        var job = new ProvisioningJob
        {
            RequestedByUserId = userId,
            VirtualGameInstanceId = instance.Id,
            Operation = ProvisioningOperation.UpdateVmResources,
            Status = ProvisioningJobStatus.Queued,
            PayloadJson = CreatePayloadJson(new JobPayload
            {
                IdempotencyKey = request.IdempotencyKey?.Trim(),
                CpuCores = request.CpuCores,
                MemoryMb = request.MemoryMb,
                DiskGb = request.DiskGb,
                RequestedVmName = request.DisplayName
            }),
            ResultJson = BuildResultJson(0, MaxRetries)
        };
        dbContext.ProvisioningJobs.Add(job);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsPendingJobConstraintViolation(ex))
        {
            throw new InvalidOperationException("This VM already has a pending operation. Wait for job completion.", ex);
        }
        await tx.CommitAsync(cancellationToken);

        return new EnqueueResult
        {
            JobId = job.Id,
            InstanceId = instance.Id,
            AlreadyExisted = false,
            Status = job.Status.ToString()
        };
    }

    public async Task<EnqueueResult> EnqueuePasswordResetAsync(
        long userId,
        bool isAdmin,
        long instanceId,
        string? requestedPassword,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        using var dbContext = new PlaymoxContext();
        await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var instance = await GetOwnedInstanceAsync(dbContext, userId, isAdmin, instanceId, cancellationToken);
        if (instance == null)
        {
            throw new InvalidOperationException("Instance not found.");
        }

        if (instance.Status == VirtualInstanceStatus.Deleted)
        {
            throw new InvalidOperationException("Cannot reset password for a deleted instance.");
        }

        await EnsureNoPendingJobsForInstanceAsync(dbContext, instance.Id, cancellationToken);

        var existing = await FindExistingIdempotentJobAsync(
            dbContext,
            userId,
            ProvisioningOperation.ResetVmPassword,
            instance.Id,
            idempotencyKey,
            cancellationToken);
        if (existing != null)
        {
            return EnqueueResult.FromExisting(existing);
        }

        var accessUsername = TryGetAccessUsername(instance.ConfigurationJson) ?? "gameserver";
        var normalizedRequestedPassword = string.IsNullOrWhiteSpace(requestedPassword)
            ? null
            : requestedPassword.Trim();
        if (normalizedRequestedPassword != null && (normalizedRequestedPassword.Length < 8 || normalizedRequestedPassword.Length > 64))
        {
            throw new InvalidOperationException("Custom password length must be between 8 and 64 characters.");
        }
        var accessPassword = normalizedRequestedPassword ?? GenerateSecurePassword();
        var encryptedPassword = SecureSecret.Encrypt(accessPassword);

        var job = new ProvisioningJob
        {
            RequestedByUserId = userId,
            VirtualGameInstanceId = instance.Id,
            Operation = ProvisioningOperation.ResetVmPassword,
            Status = ProvisioningJobStatus.Queued,
            PayloadJson = CreatePayloadJson(new JobPayload
            {
                IdempotencyKey = idempotencyKey?.Trim(),
                AccessUsername = accessUsername,
                AccessPasswordEncrypted = encryptedPassword
            }),
            ResultJson = BuildResultJson(0, MaxRetries)
        };
        dbContext.ProvisioningJobs.Add(job);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsPendingJobConstraintViolation(ex))
        {
            throw new InvalidOperationException("This VM already has a pending operation. Wait for job completion.", ex);
        }
        await tx.CommitAsync(cancellationToken);

        return new EnqueueResult
        {
            JobId = job.Id,
            InstanceId = instance.Id,
            AlreadyExisted = false,
            Status = job.Status.ToString()
        };
    }

    public async Task<EnqueueResult> EnqueueGameReinstallAsync(
        long userId,
        bool isAdmin,
        long instanceId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return await EnqueueGameConfigJobAsync(
            userId,
            isAdmin,
            instanceId,
            ProvisioningOperation.ReinstallGameServer,
            null,
            idempotencyKey,
            cancellationToken);
    }

    public async Task<EnqueueResult> EnqueueGameConfigurationUpdateAsync(
        long userId,
        bool isAdmin,
        long instanceId,
        string configurationJson,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            throw new InvalidOperationException("Configuration JSON is required.");
        }

        try
        {
            using var parsed = JsonDocument.Parse(configurationJson);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Configuration JSON must be an object.");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Configuration JSON is invalid.", ex);
        }

        return await EnqueueGameConfigJobAsync(
            userId,
            isAdmin,
            instanceId,
            ProvisioningOperation.UpdateGameConfiguration,
            configurationJson,
            idempotencyKey,
            cancellationToken);
    }

    public async Task<GameServerConfigurationSnapshot> GetGameConfigurationAsync(
        long userId,
        bool isAdmin,
        long instanceId,
        bool refreshFromServer,
        CancellationToken cancellationToken = default)
    {
        using var dbContext = new PlaymoxContext();
        var instance = await dbContext.VirtualGameInstances
            .AsNoTracking()
            .Include(x => x.GameDefinition)
            .FirstOrDefaultAsync(x => x.Id == instanceId && (isAdmin || x.OwnerId == userId), cancellationToken);
        if (instance == null)
        {
            throw new InvalidOperationException("Instance not found.");
        }

        var connector = gameServerConnectorRegistry.Resolve(instance.GameDefinition);
        if (connector == null)
        {
            return new GameServerConfigurationSnapshot
            {
                InstanceId = instance.Id,
                Source = "db",
                ConfigurationJson = ExtractUserConfigurationJson(instance.ConfigurationJson) ?? "{}"
            };
        }

        return await connector.GetConfigurationSnapshotAsync(
            instance,
            ExtractUserConfigurationJson(instance.ConfigurationJson),
            refreshFromServer,
            cancellationToken);
    }

    public async Task<GameServerBackupArchive> CreateGameBackupAsync(
        long userId,
        bool isAdmin,
        long instanceId,
        CancellationToken cancellationToken = default)
    {
        using var dbContext = new PlaymoxContext();
        var instance = await dbContext.VirtualGameInstances
            .AsNoTracking()
            .Include(x => x.GameDefinition)
            .FirstOrDefaultAsync(x => x.Id == instanceId && (isAdmin || x.OwnerId == userId), cancellationToken);
        if (instance == null)
        {
            throw new InvalidOperationException("Instance not found.");
        }

        if (instance.Status == VirtualInstanceStatus.Deleted)
        {
            throw new InvalidOperationException("Cannot operate on a deleted instance.");
        }

        var connector = gameServerConnectorRegistry.Resolve(instance.GameDefinition);
        if (connector == null)
        {
            throw new InvalidOperationException("Backup is not supported for this game.");
        }

        var backup = await connector.CreateBackupAsync(instance, cancellationToken);
        if (backup == null || backup.Content.Length == 0)
        {
            throw new InvalidOperationException("Backup is empty or unavailable.");
        }

        return backup;
    }

    public async Task RestoreGameBackupAsync(
        long userId,
        bool isAdmin,
        long instanceId,
        string archiveFilePath,
        long archiveSizeBytes,
        CancellationToken cancellationToken = default)
    {
        if (archiveSizeBytes <= 0)
        {
            throw new InvalidOperationException("Backup archive is empty.");
        }

        using var dbContext = new PlaymoxContext();
        var instance = await dbContext.VirtualGameInstances
            .Include(x => x.GameDefinition)
            .FirstOrDefaultAsync(x => x.Id == instanceId && (isAdmin || x.OwnerId == userId), cancellationToken);
        if (instance == null)
        {
            throw new InvalidOperationException("Instance not found.");
        }

        if (instance.Status == VirtualInstanceStatus.Deleted)
        {
            throw new InvalidOperationException("Cannot operate on a deleted instance.");
        }

        await EnsureNoPendingJobsForInstanceAsync(dbContext, instance.Id, cancellationToken);

        var connector = gameServerConnectorRegistry.Resolve(instance.GameDefinition);
        if (connector == null)
        {
            throw new InvalidOperationException("Restore is not supported for this game.");
        }

        await connector.RestoreBackupAsync(instance, archiveFilePath, archiveSizeBytes, cancellationToken);
        var snapshot = await connector.GetConfigurationSnapshotAsync(
            instance,
            instance.ConfigurationJson,
            refreshFromServer: true,
            cancellationToken);
        var accessUsername = TryGetAccessUsername(instance.ConfigurationJson ?? string.Empty);
        var accessPasswordEncrypted = TryGetAccessPasswordEncrypted(instance.ConfigurationJson ?? string.Empty);
        var steamUsername = TryGetSteamUsername(instance.ConfigurationJson ?? string.Empty);
        var steamPasswordEncrypted = TryGetSteamPasswordEncrypted(instance.ConfigurationJson ?? string.Empty);
        if (!string.Equals(snapshot.Source, "db-fallback", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(snapshot.ConfigurationJson))
        {
            if (!string.IsNullOrWhiteSpace(accessUsername) && !string.IsNullOrWhiteSpace(accessPasswordEncrypted))
            {
                instance.ConfigurationJson = BuildMergedConfigurationJson(
                    snapshot.ConfigurationJson,
                    accessUsername,
                    accessPasswordEncrypted,
                    steamUsername,
                    steamPasswordEncrypted);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
    }

    public async Task<List<InstanceListItem>> GetInstancesForUserAsync(long userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        using var dbContext = new PlaymoxContext();
        var query = dbContext.VirtualGameInstances
            .AsNoTracking()
            .Include(x => x.GameDefinition)
            .Include(x => x.Owner)
            .Where(x => isAdmin || x.OwnerId == userId)
            .OrderByDescending(x => x.Id);

        var instances = await query.ToListAsync(cancellationToken);
        var instanceIds = instances.Select(x => x.Id).ToList();
        var latestJobs = await dbContext.ProvisioningJobs.AsNoTracking()
            .Where(x => x.VirtualGameInstanceId != null && instanceIds.Contains(x.VirtualGameInstanceId.Value))
            .GroupBy(x => x.VirtualGameInstanceId!.Value)
            .Select(g => g.OrderByDescending(x => x.Id).First())
            .ToListAsync(cancellationToken);
        var jobsByInstance = latestJobs.ToDictionary(x => x.VirtualGameInstanceId!.Value, x => x);

        var settings = await dbContext.ProxmoxSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        var globalExternalAddress = settings?.ExternalAddress;

        return instances.Select(instance =>
        {
            jobsByInstance.TryGetValue(instance.Id, out var latestJob);
            var canSeeSshCredentials = isAdmin || instance.SshPublicPort.HasValue;
            return new InstanceListItem
            {
                Id = instance.Id,
                OwnerId = instance.OwnerId,
                OwnerLogin = instance.Owner?.Login,
                VmId = instance.VmId,
                InstanceKind = instance.InstanceKind.ToString(),
                CpuCores = instance.CpuCores,
                MemoryMb = instance.MemoryMb,
                DiskGb = instance.DiskGb,
                Status = instance.Status.ToString(),
                StatusLabel = instance.Status.GetDescription(),
                GameDefinitionId = instance.GameDefinitionId,
                GameDisplayName = instance.GameDefinition?.DisplayName ?? "Unknown",
                VmDisplayName = instance.VmDisplayName,
                LastJobId = latestJob?.Id,
                LastJobStatus = latestJob?.Status.ToString(),
                LastJobStatusLabel = latestJob?.Status.GetDescription(),
                LastJobOperation = latestJob?.Operation.ToString(),
                LastJobOperationLabel = latestJob?.Operation.GetDescription(),
                LastJobErrorMessage = latestJob?.ErrorMessage,
                HasPendingJob = latestJob is { Status: ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running },
                AccessUsername = canSeeSshCredentials ? TryGetAccessUsername(instance.ConfigurationJson) : null,
                AccessPassword = canSeeSshCredentials ? TryGetAccessPassword(instance.ConfigurationJson) : null,
                PublicHost = !string.IsNullOrWhiteSpace(globalExternalAddress) ? globalExternalAddress : instance.PublicHost,
                SshPublicPort = instance.SshPublicPort,
                GamePublicPortStart = instance.GamePublicPortStart,
                GamePublicPortCount = instance.GamePublicPortCount,
                GamePublicPortProtocol = instance.GamePublicPortProtocol
            };
        }).ToList();
    }

    public async Task<VmGameRuntimeInfo> GetGameRuntimeInfoAsync(
        long userId,
        bool isAdmin,
        long instanceId,
        CancellationToken cancellationToken = default)
    {
        using var dbContext = new PlaymoxContext();
        var instance = await dbContext.VirtualGameInstances
            .AsNoTracking()
            .Include(x => x.GameDefinition)
            .FirstOrDefaultAsync(x => x.Id == instanceId && (isAdmin || x.OwnerId == userId), cancellationToken);
        if (instance == null)
        {
            throw new InvalidOperationException("Instance not found.");
        }

        var configuredVersion = TryGetUserConfigString(instance.ConfigurationJson, "serverVersion");
        var connector = gameServerConnectorRegistry.Resolve(instance.GameDefinition);
        if (connector == null)
        {
            return new VmGameRuntimeInfo
            {
                InstanceId = instance.Id,
                VmId = instance.VmId,
                Status = "Unsupported",
                IsRunning = false,
                Message = $"Runtime preview is not available for game: {instance.GameDefinition?.Key ?? "unknown"}.",
                ConfiguredServerVersion = configuredVersion
            };
        }

        var runtime = await connector.GetRuntimeInfoAsync(instance, cancellationToken);
        runtime.ConfiguredServerVersion = configuredVersion;
        return runtime;
    }

    public async Task<LiveCommandStreamItem> GetLiveCommandStreamAsync(
        long userId,
        bool isAdmin,
        long instanceId,
        CancellationToken cancellationToken = default)
    {
        using var dbContext = new PlaymoxContext();
        var instance = await dbContext.VirtualGameInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == instanceId && (isAdmin || x.OwnerId == userId), cancellationToken);
        if (instance == null)
        {
            throw new InvalidOperationException("Instance not found.");
        }

        var live = proxmoxAutomationClient.GetLiveGuestScriptSnapshot(instance.VmId);
        var recent = proxmoxAutomationClient.GetRecentGuestScriptSnapshots(instance.VmId);
        if (live == null)
        {
            return new LiveCommandStreamItem
            {
                InstanceId = instance.Id,
                VmId = instance.VmId,
                Active = false,
                Message = "No active command stream for this VM.",
                RecentItems = recent.Select(MapLiveSnapshot).ToList()
            };
        }

        var output = MergeLiveOutput(live);

        return new LiveCommandStreamItem
        {
            InstanceId = instance.Id,
            VmId = instance.VmId,
            Active = live.IsRunning,
            Pid = live.Pid,
            CommandSummary = live.CommandSummary,
            ExitCode = live.ExitCode,
            StartedAtUtc = live.StartedAtUtc,
            UpdatedAtUtc = live.UpdatedAtUtc,
            CompletedAtUtc = live.CompletedAtUtc,
            Output = output ?? string.Empty,
            RecentItems = recent.Select(MapLiveSnapshot).ToList()
        };
    }

    private static string MergeLiveOutput(GuestScriptLiveSnapshot live)
    {
        var output = string.Empty;
        if (!string.IsNullOrWhiteSpace(live.StdOut))
        {
            output += live.StdOut;
        }

        if (!string.IsNullOrWhiteSpace(live.StdErr))
        {
            if (!string.IsNullOrWhiteSpace(output))
            {
                output += Environment.NewLine;
            }

            output += live.StdErr;
        }

        return output;
    }

    private static LiveCommandStreamEntry MapLiveSnapshot(GuestScriptLiveSnapshot snapshot)
    {
        return new LiveCommandStreamEntry
        {
            VmId = snapshot.VmId,
            Pid = snapshot.Pid,
            CommandSummary = snapshot.CommandSummary,
            Active = snapshot.IsRunning,
            ExitCode = snapshot.ExitCode,
            StartedAtUtc = snapshot.StartedAtUtc,
            UpdatedAtUtc = snapshot.UpdatedAtUtc,
            CompletedAtUtc = snapshot.CompletedAtUtc,
            Output = MergeLiveOutput(snapshot)
        };
    }

    public async Task<SshSessionAccess> GetSshSessionAccessAsync(
        long userId,
        bool isAdmin,
        long instanceId,
        CancellationToken cancellationToken = default)
    {
        using var dbContext = new PlaymoxContext();
        var instance = await dbContext.VirtualGameInstances
            .FirstOrDefaultAsync(x => x.Id == instanceId && (isAdmin || x.OwnerId == userId), cancellationToken);
        if (instance == null)
        {
            throw new InvalidOperationException("Instance not found.");
        }

        if (instance.Status == VirtualInstanceStatus.Deleted)
        {
            throw new InvalidOperationException("Cannot open SSH for a deleted instance.");
        }

        var username = TryGetAccessUsername(instance.ConfigurationJson ?? string.Empty);
        var password = TryGetAccessPassword(instance.ConfigurationJson ?? string.Empty);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("SSH credentials are missing for this instance.");
        }

        var storedHost = instance.InternalIp?.Trim();
        var resolvedHost = await ResolveVmInternalIpAsync(instance.VmId, cancellationToken);
        var hostCandidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(resolvedHost))
        {
            hostCandidates.Add(resolvedHost.Trim());
        }

        if (!string.IsNullOrWhiteSpace(storedHost) &&
            hostCandidates.All(x => !string.Equals(x, storedHost, StringComparison.OrdinalIgnoreCase)))
        {
            hostCandidates.Add(storedHost);
        }

        if (!string.IsNullOrWhiteSpace(resolvedHost) &&
            !string.Equals(storedHost, resolvedHost, StringComparison.OrdinalIgnoreCase))
        {
            instance.InternalIp = resolvedHost.Trim();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var host = hostCandidates.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("VM internal IP is unavailable.");
        }

        return new SshSessionAccess
        {
            InstanceId = instance.Id,
            VmId = instance.VmId,
            Host = host,
            Username = username.Trim(),
            Password = password,
            CandidateHosts = hostCandidates
        };
    }

    public async Task<SshTerminalPreviewItem> GetSshTerminalPreviewAsync(
        long userId,
        bool isAdmin,
        long instanceId,
        int tailLines = 250,
        CancellationToken cancellationToken = default)
    {
        using var dbContext = new PlaymoxContext();
        var instance = await dbContext.VirtualGameInstances
            .AsNoTracking()
            .Include(x => x.GameDefinition)
            .FirstOrDefaultAsync(x => x.Id == instanceId && (isAdmin || x.OwnerId == userId), cancellationToken);
        if (instance == null)
        {
            throw new InvalidOperationException("Instance not found.");
        }

        if (instance.Status == VirtualInstanceStatus.Deleted)
        {
            return new SshTerminalPreviewItem
            {
                InstanceId = instance.Id,
                Available = false,
                Message = "VM is deleted."
            };
        }

        var gameKey = (instance.GameDefinition?.Key ?? string.Empty).Trim().ToLowerInvariant();
        var command = BuildSshPreviewCommand(gameKey, tailLines);
        if (string.IsNullOrWhiteSpace(command))
        {
            return new SshTerminalPreviewItem
            {
                InstanceId = instance.Id,
                Available = false,
                Message = "SSH preview is not available for this game."
            };
        }

        var username = TryGetAccessUsername(instance.ConfigurationJson ?? string.Empty);
        var password = TryGetAccessPassword(instance.ConfigurationJson ?? string.Empty);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return new SshTerminalPreviewItem
            {
                InstanceId = instance.Id,
                Available = false,
                Message = "Missing SSH credentials in VM configuration."
            };
        }

        var host = instance.InternalIp?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            host = await ResolveVmInternalIpAsync(instance.VmId, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            return new SshTerminalPreviewItem
            {
                InstanceId = instance.Id,
                Available = false,
                Message = "VM does not have a resolved internal IP address."
            };
        }

        try
        {
            using var ssh = CreateSshClient(host, username, password);
            ssh.Connect();
            using var previewCmd = ssh.CreateCommand(NormalizeShellCommand(command));
            previewCmd.CommandTimeout = TimeSpan.FromSeconds(45);
            previewCmd.Execute();

            var output = string.IsNullOrWhiteSpace(previewCmd.Result)
                ? previewCmd.Error
                : previewCmd.Result;

            return new SshTerminalPreviewItem
            {
                InstanceId = instance.Id,
                Available = true,
                Source = "ssh",
                GeneratedAtUtc = DateTime.UtcNow,
                Output = string.IsNullOrWhiteSpace(output) ? "(No output to display)" : output.TrimEnd()
            };
        }
        catch (SshAuthenticationException)
        {
            return new SshTerminalPreviewItem
            {
                InstanceId = instance.Id,
                Available = false,
                Message = "SSH authentication to VM failed."
            };
        }
        catch (SshConnectionException)
        {
            return new SshTerminalPreviewItem
            {
                InstanceId = instance.Id,
                Available = false,
                Message = "Failed to connect to VM via SSH."
            };
        }
    }

    public async Task<ProvisioningJobStatusItem?> GetJobAsync(long userId, bool isAdmin, long jobId, CancellationToken cancellationToken = default)
    {
        using var dbContext = new PlaymoxContext();
        var job = await dbContext.ProvisioningJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == jobId, cancellationToken);
        if (job == null)
        {
            return null;
        }

        if (!isAdmin && job.RequestedByUserId != userId)
        {
            return null;
        }

        if (!isAdmin && job.VirtualGameInstanceId.HasValue)
        {
            var ownsInstance = await dbContext.VirtualGameInstances
                .AsNoTracking()
                .AnyAsync(x => x.Id == job.VirtualGameInstanceId.Value && x.OwnerId == userId, cancellationToken);
            if (!ownsInstance)
            {
                return null;
            }
        }

        var retry = ParseRetryState(job.ResultJson);
        return new ProvisioningJobStatusItem
        {
            Id = job.Id,
            VirtualGameInstanceId = job.VirtualGameInstanceId,
            Operation = job.Operation.ToString(),
            OperationLabel = job.Operation.GetDescription(),
            Status = job.Status.ToString(),
            StatusLabel = job.Status.GetDescription(),
            StartedDateUtc = job.StartedDateUtc,
            CompletedDateUtc = job.CompletedDateUtc,
            ErrorMessage = job.ErrorMessage,
            RetryCount = retry.RetryCount,
            MaxRetries = retry.MaxRetries
        };
    }

    public async Task<List<ProvisioningJobStatusItem>> GetJobsAsync(long userId, bool isAdmin, long? instanceId, CancellationToken cancellationToken = default)
    {
        using var dbContext = new PlaymoxContext();
        var query = dbContext.ProvisioningJobs.AsNoTracking().OrderByDescending(x => x.Id).AsQueryable();
        if (!isAdmin)
        {
            var ownedInstanceIds = dbContext.VirtualGameInstances.AsNoTracking()
                .Where(x => x.OwnerId == userId)
                .Select(x => x.Id);
            query = query.Where(x => x.RequestedByUserId == userId ||
                                     (x.VirtualGameInstanceId.HasValue && ownedInstanceIds.Contains(x.VirtualGameInstanceId.Value)));
        }

        if (instanceId.HasValue)
        {
            if (!isAdmin)
            {
                var ownsInstance = await dbContext.VirtualGameInstances
                    .AsNoTracking()
                    .AnyAsync(x => x.Id == instanceId.Value && x.OwnerId == userId, cancellationToken);
                if (!ownsInstance)
                {
                    throw new InvalidOperationException("Instance not found.");
                }
            }

            query = query.Where(x => x.VirtualGameInstanceId == instanceId.Value);
        }

        var jobs = await query.Take(100).ToListAsync(cancellationToken);
        return jobs.Select(job =>
        {
            var retry = ParseRetryState(job.ResultJson);
            return new ProvisioningJobStatusItem
            {
                Id = job.Id,
                VirtualGameInstanceId = job.VirtualGameInstanceId,
                Operation = job.Operation.ToString(),
                OperationLabel = job.Operation.GetDescription(),
                Status = job.Status.ToString(),
                StatusLabel = job.Status.GetDescription(),
                StartedDateUtc = job.StartedDateUtc,
                CompletedDateUtc = job.CompletedDateUtc,
                ErrorMessage = job.ErrorMessage,
                RetryCount = retry.RetryCount,
                MaxRetries = retry.MaxRetries
            };
        }).ToList();
    }

    public static (int RetryCount, int MaxRetries) ParseRetryState(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return (0, MaxRetries);
        }

        try
        {
            using var json = JsonDocument.Parse(resultJson);
            var retryCount = json.RootElement.TryGetProperty("retryCount", out var retryProp) && retryProp.TryGetInt32(out var retry)
                ? retry
                : 0;
            var maxRetries = json.RootElement.TryGetProperty("maxRetries", out var maxProp) && maxProp.TryGetInt32(out var max)
                ? max
                : MaxRetries;
            return (retryCount, maxRetries);
        }
        catch
        {
            return (0, MaxRetries);
        }
    }

    public static string BuildResultJson(int retryCount, int maxRetries, string? lastTaskId = null)
    {
        return JsonSerializer.Serialize(new
        {
            retryCount,
            maxRetries,
            lastTaskId
        });
    }

    public static JobPayload ParsePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new JobPayload();
        }

        return JsonSerializer.Deserialize<JobPayload>(payloadJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new JobPayload();
    }

    public static string CreatePayloadJson(JobPayload payload)
    {
        return JsonSerializer.Serialize(payload);
    }

    private static string BuildVmName(long userId, string gameKey, string? requestedDisplayName)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedDisplayName) ? gameKey : requestedDisplayName.Trim();
        var normalized = new string(baseName
            .ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '-')
            .ToArray());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "game";
        }

        return $"playmox-u{userId}-{normalized}-{DateTime.UtcNow:yyyyMMddHHmmss}";
    }

    private static async Task<VirtualGameInstance?> GetOwnedInstanceAsync(
        PlaymoxContext dbContext,
        long userId,
        bool isAdmin,
        long instanceId,
        CancellationToken cancellationToken)
    {
        return await dbContext.VirtualGameInstances
            .FirstOrDefaultAsync(x => x.Id == instanceId && (isAdmin || x.OwnerId == userId), cancellationToken);
    }

    private static void EnsureCreationWithinQuota(
        UserQuota quota,
        List<VirtualGameInstance> quotaInstances,
        List<VirtualGameInstance> diskQuotaInstances,
        int cpuCores,
        int memoryMb,
        int? diskGb,
        int requiredGamePorts)
    {
        var totalInstances = quotaInstances.Count + 1;
        if (totalInstances > quota.MaxInstancesLimit)
        {
            throw new InvalidOperationException("Instance count limit would be exceeded.");
        }

        var totalCpu = quotaInstances.Sum(x => x.CpuCores) + cpuCores;
        if (totalCpu > quota.CpuCoresLimit)
        {
            throw new InvalidOperationException("CPU quota would be exceeded.");
        }

        var totalMemory = quotaInstances.Sum(x => x.MemoryMb) + memoryMb;
        if (totalMemory > quota.MemoryMbLimit)
        {
            throw new InvalidOperationException("Memory quota would be exceeded.");
        }

        if (quota.DiskGbLimit.HasValue)
        {
            var totalDisk = diskQuotaInstances.Sum(x => x.DiskGb.GetValueOrDefault()) + diskGb.GetValueOrDefault();
            if (totalDisk > quota.DiskGbLimit.Value)
            {
                throw new InvalidOperationException("Disk quota would be exceeded.");
            }
        }

        if (quota.MaxPublicPortsLimit.HasValue)
        {
            var usedPublicPorts = quotaInstances.Sum(x => x.GamePublicPortCount.GetValueOrDefault());
            if (usedPublicPorts + requiredGamePorts > quota.MaxPublicPortsLimit.Value)
            {
                throw new InvalidOperationException("Public ports quota would be exceeded for this game.");
            }
        }
    }

    private static void EnsureUpdateWithinQuota(
        UserQuota quota,
        List<VirtualGameInstance> quotaInstances,
        List<VirtualGameInstance> diskQuotaInstances,
        VirtualGameInstance updatingInstance,
        int cpuCores,
        int memoryMb,
        int? diskGb)
    {
        var others = quotaInstances.Where(x => x.Id != updatingInstance.Id).ToList();
        var totalCpu = others.Sum(x => x.CpuCores) + cpuCores;
        if (totalCpu > quota.CpuCoresLimit)
        {
            throw new InvalidOperationException("CPU quota would be exceeded.");
        }

        var totalMemory = others.Sum(x => x.MemoryMb) + memoryMb;
        if (totalMemory > quota.MemoryMbLimit)
        {
            throw new InvalidOperationException("Memory quota would be exceeded.");
        }

        if (quota.DiskGbLimit.HasValue)
        {
            var diskOthers = diskQuotaInstances.Where(x => x.Id != updatingInstance.Id).ToList();
            var totalDisk = diskOthers.Sum(x => x.DiskGb.GetValueOrDefault()) + diskGb.GetValueOrDefault();
            if (totalDisk > quota.DiskGbLimit.Value)
            {
                throw new InvalidOperationException("Disk quota would be exceeded.");
            }
        }
    }

    private static async Task<ProvisioningJob?> FindExistingIdempotentJobAsync(
        PlaymoxContext dbContext,
        long userId,
        ProvisioningOperation operation,
        long? instanceId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        var keyMarker = $"\"idempotencyKey\":\"{idempotencyKey.Trim()}\"";
        var query = dbContext.ProvisioningJobs.AsNoTracking()
            .Where(x => x.RequestedByUserId == userId && x.Operation == operation)
            .Where(x => x.Status == ProvisioningJobStatus.Queued ||
                        x.Status == ProvisioningJobStatus.Running ||
                        x.Status == ProvisioningJobStatus.Succeeded)
            .Where(x => x.PayloadJson != null && x.PayloadJson.Contains(keyMarker));

        if (instanceId.HasValue)
        {
            query = query.Where(x => x.VirtualGameInstanceId == instanceId.Value);
        }

        return await query.OrderByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task EnsureNoPendingJobsForInstanceAsync(
        PlaymoxContext dbContext,
        long instanceId,
        CancellationToken cancellationToken)
    {
        var hasPending = await dbContext.ProvisioningJobs.AsNoTracking()
            .AnyAsync(
                x => x.VirtualGameInstanceId == instanceId &&
                     (x.Status == ProvisioningJobStatus.Queued || x.Status == ProvisioningJobStatus.Running),
                cancellationToken);
        if (hasPending)
        {
            throw new InvalidOperationException("This VM already has a pending operation. Wait for job completion.");
        }
    }

    private async Task EnsureStartWithinQuotaAsync(PlaymoxContext dbContext, VirtualGameInstance instance, CancellationToken cancellationToken)
    {
        if (instance.Status != VirtualInstanceStatus.Stopped)
        {
            return;
        }

        var quota = await dbContext.UsersQuotas.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == instance.OwnerId, cancellationToken);
        if (quota == null)
        {
            throw new InvalidOperationException("User quota is not configured.");
        }

        var quotaInstances = await dbContext.VirtualGameInstances.AsNoTracking()
            .Where(x => x.OwnerId == instance.OwnerId && QuotaComputeStatuses.Contains(x.Status))
            .ToListAsync(cancellationToken);

        var totalInstances = quotaInstances.Count + 1;
        if (totalInstances > quota.MaxInstancesLimit)
        {
            throw new InvalidOperationException("Cannot start VM: instance count quota would be exceeded.");
        }

        var totalCpu = quotaInstances.Sum(x => x.CpuCores) + instance.CpuCores;
        if (totalCpu > quota.CpuCoresLimit)
        {
            throw new InvalidOperationException("Cannot start VM: CPU quota would be exceeded.");
        }

        var totalMemory = quotaInstances.Sum(x => x.MemoryMb) + instance.MemoryMb;
        if (totalMemory > quota.MemoryMbLimit)
        {
            throw new InvalidOperationException("Cannot start VM: memory quota would be exceeded.");
        }

        if (quota.MaxPublicPortsLimit.HasValue)
        {
            var usedPorts = quotaInstances.Sum(x => x.GamePublicPortCount.GetValueOrDefault());
            var instancePorts = instance.GamePublicPortCount.GetValueOrDefault();
            if (usedPorts + instancePorts > quota.MaxPublicPortsLimit.Value)
            {
                throw new InvalidOperationException("Cannot start VM: public ports quota would be exceeded.");
            }
        }
    }

    private async Task<EnqueueResult> EnqueueGameConfigJobAsync(
        long userId,
        bool isAdmin,
        long instanceId,
        ProvisioningOperation operation,
        string? configurationJson,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var dbContext = new PlaymoxContext();
        await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var instance = await GetOwnedInstanceAsync(dbContext, userId, isAdmin, instanceId, cancellationToken);
        if (instance == null)
        {
            throw new InvalidOperationException("Instance not found.");
        }

        if (instance.Status == VirtualInstanceStatus.Deleted)
        {
            throw new InvalidOperationException("Cannot operate on a deleted instance.");
        }

        await EnsureNoPendingJobsForInstanceAsync(dbContext, instance.Id, cancellationToken);
        var existing = await FindExistingIdempotentJobAsync(
            dbContext,
            userId,
            operation,
            instance.Id,
            idempotencyKey,
            cancellationToken);
        if (existing != null)
        {
            return EnqueueResult.FromExisting(existing);
        }

        var payload = new JobPayload
        {
            IdempotencyKey = idempotencyKey?.Trim()
        };
        if (!string.IsNullOrWhiteSpace(configurationJson))
        {
            payload.ConfigurationJson = configurationJson;
        }

        var job = new ProvisioningJob
        {
            RequestedByUserId = userId,
            VirtualGameInstanceId = instance.Id,
            Operation = operation,
            Status = ProvisioningJobStatus.Queued,
            PayloadJson = CreatePayloadJson(payload),
            ResultJson = BuildResultJson(0, MaxRetries)
        };
        dbContext.ProvisioningJobs.Add(job);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsPendingJobConstraintViolation(ex))
        {
            throw new InvalidOperationException("This VM already has a pending operation. Wait for job completion.", ex);
        }
        await tx.CommitAsync(cancellationToken);
        return new EnqueueResult
        {
            JobId = job.Id,
            InstanceId = instance.Id,
            AlreadyExisted = false,
            Status = job.Status.ToString()
        };
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

    public static string? ExtractUserConfigurationJson(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(configurationJson);
            if (json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("user", out var userConfig))
            {
                return userConfig.GetRawText();
            }
        }
        catch
        {
            // ignore malformed config
        }

        return configurationJson;
    }

    private static string? TryGetAccessUsername(string configurationJson)
    {
        try
        {
            using var json = JsonDocument.Parse(configurationJson);
            if (json.RootElement.TryGetProperty("systemAccess", out var access) &&
                access.TryGetProperty("username", out var usernameProp) &&
                usernameProp.ValueKind == JsonValueKind.String)
            {
                return usernameProp.GetString();
            }
        }
        catch
        {
            // ignore malformed config
        }

        return null;
    }

    private static string? TryGetAccessPassword(string configurationJson)
    {
        try
        {
            using var json = JsonDocument.Parse(configurationJson);
            if (!json.RootElement.TryGetProperty("systemAccess", out var access))
            {
                return null;
            }

            if (!access.TryGetProperty("passwordEncrypted", out var encryptedProp) || encryptedProp.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var encrypted = encryptedProp.GetString();
            if (string.IsNullOrWhiteSpace(encrypted))
            {
                return null;
            }

            return SecureSecret.Decrypt(encrypted);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetAccessPasswordEncrypted(string configurationJson)
    {
        try
        {
            using var json = JsonDocument.Parse(configurationJson);
            if (!json.RootElement.TryGetProperty("systemAccess", out var access))
            {
                return null;
            }

            if (!access.TryGetProperty("passwordEncrypted", out var encryptedProp) || encryptedProp.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var encrypted = encryptedProp.GetString();
            return string.IsNullOrWhiteSpace(encrypted) ? null : encrypted;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetSteamUsername(string configurationJson)
    {
        try
        {
            using var json = JsonDocument.Parse(configurationJson);
            if (!json.RootElement.TryGetProperty("systemSteam", out var steam))
            {
                return null;
            }

            if (!steam.TryGetProperty("username", out var usernameProp) || usernameProp.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var username = usernameProp.GetString();
            return string.IsNullOrWhiteSpace(username) ? null : username;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetSteamPasswordEncrypted(string configurationJson)
    {
        try
        {
            using var json = JsonDocument.Parse(configurationJson);
            if (!json.RootElement.TryGetProperty("systemSteam", out var steam))
            {
                return null;
            }

            if (!steam.TryGetProperty("passwordEncrypted", out var passProp) || passProp.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var encrypted = passProp.GetString();
            return string.IsNullOrWhiteSpace(encrypted) ? null : encrypted;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetUserConfigString(string? configurationJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(configurationJson);
            if (!json.RootElement.TryGetProperty("user", out var userConfig) ||
                userConfig.ValueKind != JsonValueKind.Object ||
                !userConfig.TryGetProperty(propertyName, out var value) ||
                value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return value.GetString();
        }
        catch
        {
            return null;
        }
    }

    private static string GenerateSecurePassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
        const int passwordLength = 8;
        Span<byte> bytes = stackalloc byte[passwordLength];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var charsArray = new char[passwordLength];
        for (var i = 0; i < bytes.Length; i++)
        {
            charsArray[i] = chars[bytes[i] % chars.Length];
        }

        return new string(charsArray);
    }

    private async Task<int> GetUniqueVmIdAsync(PlaymoxContext dbContext, CancellationToken cancellationToken)
    {
        var proxmoxNext = await proxmoxAutomationClient.GetNextVmIdAsync(cancellationToken);
        var dbMax = await dbContext.VirtualGameInstances
            .AsNoTracking()
            .Select(x => (int?)x.VmId)
            .MaxAsync(cancellationToken) ?? 0;
        var candidate = Math.Max(proxmoxNext, dbMax + 1);

        while (true)
        {
            var usedInDb = await dbContext.VirtualGameInstances.AsNoTracking()
                .AnyAsync(x => x.VmId == candidate, cancellationToken);
            if (usedInDb || await proxmoxAutomationClient.VmExistsAsync(candidate, cancellationToken))
            {
                candidate++;
                continue;
            }

            return candidate;
        }
    }

    private static bool IsDuplicateVmIdError(DbUpdateException ex)
    {
        if (ex.InnerException is not SqlException sqlEx)
        {
            return false;
        }

        return sqlEx.Number is 2601 or 2627 &&
               sqlEx.Message.Contains("IX_VirtualGameInstances_VmId", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDuplicatePortLeaseError(DbUpdateException ex)
    {
        if (ex.InnerException is not SqlException sqlEx)
        {
            return false;
        }

        return sqlEx.Number is 2601 or 2627 &&
               sqlEx.Message.Contains("IX_PortLeases_Port", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPendingJobConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is not SqlException sqlEx)
        {
            return false;
        }

        return sqlEx.Number is 2601 or 2627 &&
               sqlEx.Message.Contains("UX_ProvisioningJobs_ActivePerInstanceAndStatus", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(int? SshPort, int? GamePortStart, int? GamePortCount)> AllocateAndReservePublicPortsAsync(
        PlaymoxContext dbContext,
        long instanceId,
        int requiredGamePorts,
        bool allowSshPublicPort,
        CancellationToken cancellationToken)
    {
        int? sshPort = null;
        var reservedPorts = new List<int>();
        try
        {
            if (allowSshPublicPort)
            {
                sshPort = await ReserveNextFreePortAsync(dbContext, instanceId, cancellationToken);
                reservedPorts.Add(sshPort.Value);
            }

            if (requiredGamePorts <= 0)
            {
                return (sshPort, null, null);
            }

            var gameStart = await ReserveContiguousPortRangeAsync(dbContext, instanceId, requiredGamePorts, cancellationToken);
            for (var port = gameStart; port < gameStart + requiredGamePorts; port++)
            {
                reservedPorts.Add(port);
            }

            return (sshPort, gameStart, requiredGamePorts);
        }
        catch
        {
            if (reservedPorts.Count > 0)
            {
                await ReleaseReservedPortsAsync(dbContext, instanceId, reservedPorts, cancellationToken);
            }

            throw;
        }
    }

    private static async Task BackfillPortLeasesForActiveInstancesAsync(
        PlaymoxContext dbContext,
        CancellationToken cancellationToken)
    {
        var instances = await dbContext.VirtualGameInstances.AsNoTracking()
            .Where(x => PortAllocationStatuses.Contains(x.Status) &&
                        (x.SshPublicPort.HasValue ||
                         (x.GamePublicPortStart.HasValue && x.GamePublicPortCount.GetValueOrDefault() > 0)))
            .Select(x => new
            {
                x.Id,
                x.SshPublicPort,
                x.GamePublicPortStart,
                x.GamePublicPortCount
            })
            .ToListAsync(cancellationToken);

        var leases = await dbContext.PortLeases.AsNoTracking()
            .Select(x => new { x.Port, x.VirtualGameInstanceId })
            .ToListAsync(cancellationToken);
        var leasedByPort = leases.ToDictionary(x => x.Port, x => x.VirtualGameInstanceId);

        var toInsert = new List<PortLease>();
        foreach (var instance in instances)
        {
            foreach (var port in EnumerateReservedPorts(
                         instance.SshPublicPort,
                         instance.GamePublicPortStart,
                         instance.GamePublicPortCount))
            {
                if (leasedByPort.TryGetValue(port, out var ownerId))
                {
                    if (ownerId != instance.Id)
                    {
                        throw new InvalidOperationException(
                            $"Port lease collision detected for existing instances on port {port}.");
                    }

                    continue;
                }

                leasedByPort[port] = instance.Id;
                toInsert.Add(new PortLease
                {
                    VirtualGameInstanceId = instance.Id,
                    Port = port
                });
            }
        }

        if (toInsert.Count == 0)
        {
            return;
        }

        dbContext.PortLeases.AddRange(toInsert);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicatePortLeaseError(ex))
        {
            throw new InvalidOperationException("Cannot synchronize existing port leases due to collision.", ex);
        }
    }

    private static async Task<int> ReserveNextFreePortAsync(
        PlaymoxContext dbContext,
        long instanceId,
        CancellationToken cancellationToken)
    {
        var occupiedPorts = (await dbContext.PortLeases.AsNoTracking()
            .Where(x => x.Port >= PublicPortRangeStart && x.Port <= PublicPortRangeEnd)
            .Select(x => x.Port)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        for (var port = PublicPortRangeStart; port <= PublicPortRangeEnd; port++)
        {
            if (occupiedPorts.Contains(port))
            {
                continue;
            }

            if (await TryReservePortLeaseAsync(dbContext, instanceId, port, cancellationToken))
            {
                return port;
            }

            occupiedPorts.Add(port);
        }

        throw new InvalidOperationException("No free public ports available.");
    }

    private static async Task<int> ReserveContiguousPortRangeAsync(
        PlaymoxContext dbContext,
        long instanceId,
        int requiredCount,
        CancellationToken cancellationToken)
    {
        var occupiedPorts = (await dbContext.PortLeases.AsNoTracking()
            .Where(x => x.Port >= PublicPortRangeStart && x.Port <= PublicPortRangeEnd)
            .Select(x => x.Port)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        for (var start = PublicPortRangeStart; start <= PublicPortRangeEnd - requiredCount + 1; start++)
        {
            var hasConflict = false;
            for (var offset = 0; offset < requiredCount; offset++)
            {
                if (occupiedPorts.Contains(start + offset))
                {
                    hasConflict = true;
                    break;
                }
            }

            if (hasConflict)
            {
                continue;
            }

            var reserved = new List<int>(requiredCount);
            var success = true;
            for (var offset = 0; offset < requiredCount; offset++)
            {
                var port = start + offset;
                if (!await TryReservePortLeaseAsync(dbContext, instanceId, port, cancellationToken))
                {
                    success = false;
                    occupiedPorts.Add(port);
                    break;
                }

                reserved.Add(port);
            }

            if (success)
            {
                return start;
            }

            if (reserved.Count > 0)
            {
                await ReleaseReservedPortsAsync(dbContext, instanceId, reserved, cancellationToken);
            }
        }

        throw new InvalidOperationException("No free public port range available for requested game.");
    }

    private static async Task<bool> TryReservePortLeaseAsync(
        PlaymoxContext dbContext,
        long instanceId,
        int port,
        CancellationToken cancellationToken)
    {
        var lease = new PortLease
        {
            VirtualGameInstanceId = instanceId,
            Port = port
        };
        dbContext.PortLeases.Add(lease);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsDuplicatePortLeaseError(ex))
        {
            dbContext.Entry(lease).State = EntityState.Detached;
            return false;
        }
    }

    private static async Task ReleaseReservedPortsAsync(
        PlaymoxContext dbContext,
        long instanceId,
        IReadOnlyCollection<int> ports,
        CancellationToken cancellationToken)
    {
        if (ports.Count == 0)
        {
            return;
        }

        await dbContext.PortLeases
            .Where(x => x.VirtualGameInstanceId == instanceId && ports.Contains(x.Port))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static IEnumerable<int> EnumerateReservedPorts(int? sshPort, int? gamePortStart, int? gamePortCount)
    {
        if (sshPort.HasValue)
        {
            yield return sshPort.Value;
        }

        if (!gamePortStart.HasValue || gamePortCount.GetValueOrDefault() <= 0)
        {
            yield break;
        }

        for (var port = gamePortStart.Value; port < gamePortStart.Value + gamePortCount.Value; port++)
        {
            yield return port;
        }
    }

    private static async Task<string?> ResolvePublicHostAsync(PlaymoxContext dbContext, CancellationToken cancellationToken)
    {
        var settings = await dbContext.ProxmoxSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        if (settings == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(settings.ExternalAddress))
        {
            return settings.ExternalAddress;
        }

        if (string.IsNullOrWhiteSpace(settings.ProxmoxUrl))
        {
            return null;
        }

        if (Uri.TryCreate(settings.ProxmoxUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host;
        }

        return null;
    }

    private static string? ResolveGamePortProtocol(string gameKey, int requiredGamePorts)
    {
        if (requiredGamePorts <= 0)
        {
            return null;
        }

        return string.Equals(gameKey, "terraria", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(gameKey, "unturned", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(gameKey, "factorio", StringComparison.OrdinalIgnoreCase)
            ? "udp,tcp"
            : "tcp";
    }

    private static string? BuildSshPreviewCommand(string gameKey, int tailLines)
    {
        var lines = Math.Clamp(tailLines, 50, 2000);
        var files = gameKey switch
        {
            "terraria" => new[]
            {
                "/home/gameserver/log/script/playmox-terraria-install.log",
                "/home/gameserver/log/script/terrariaserver-script.log",
                "/home/gameserver/log/console/terrariaserver-console.log"
            },
            "minecraft-java" => new[]
            {
                "/home/gameserver/log/script/playmox-minecraft-install.log",
                "/home/gameserver/log/script/mcserver-script.log",
                "/home/gameserver/log/console/mcserver-console.log"
            },
            "unturned" => new[]
            {
                "/home/gameserver/log/script/untserver-script.log",
                "/home/gameserver/log/console/untserver-console.log"
            },
            "factorio" => new[]
            {
                "/home/gameserver/log/script/fctrserver-script.log",
                "/home/gameserver/log/console/fctrserver-console.log"
            },
            _ => null
        };

        if (files == null || files.Length == 0)
        {
            return null;
        }

        var escapedFiles = string.Join(" ", files.Select(path => $"'{path}'"));
        return $"""
set -e
LINES={lines}
FOUND=0
for FILE in {escapedFiles}; do
  if [ -f "$FILE" ]; then
    FOUND=1
    echo "===== $FILE ====="
    tail -n "$LINES" "$FILE" || true
    echo
  fi
done
if [ "$FOUND" -eq 0 ]; then
  echo "No SSH installation logs available for this VM."
fi
""";
    }

    private async Task<string?> ResolveVmInternalIpAsync(int vmId, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            var ip = await TryResolveVmInternalIpWithFallbackAsync(vmId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(ip))
            {
                return ip.Trim();
            }

            if (attempt < 6)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        return null;
    }

    private async Task<string?> TryResolveVmInternalIpWithFallbackAsync(int vmId, CancellationToken cancellationToken)
    {
        try
        {
            var ip = await proxmoxAutomationClient.TryGetVmGuestIpv4Async(vmId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(ip))
            {
                return ip;
            }
        }
        catch
        {
            // ignore and try fallback script
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
            var lines = result.StdOut?.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
            var value = lines
                .FirstOrDefault(x => x.StartsWith("PLAYMOX_FALLBACK_IP=", StringComparison.Ordinal));
            return value is null ? null : value["PLAYMOX_FALLBACK_IP=".Length..];
        }
        catch
        {
            return null;
        }
    }

    private static SshClient CreateSshClient(string host, string username, string password)
    {
        var client = new SshClient(BuildConnectionInfo(host, username, password));
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
        client.KeepAliveInterval = TimeSpan.FromSeconds(15);
        return client;
    }

    private static Renci.SshNet.ConnectionInfo BuildConnectionInfo(string host, string username, string password)
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

    private static string NormalizeShellCommand(string command)
        => command.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}

public class CreateVmRequest
{
    public long GameDefinitionId { get; set; }
    public string? InstanceKind { get; set; }
    public string? DisplayName { get; set; }
    public int CpuCores { get; set; }
    public int MemoryMb { get; set; }
    public int? DiskGb { get; set; }
    public string? ConfigurationJson { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class UpdateVmResourcesRequest
{
    public int CpuCores { get; set; }
    public int MemoryMb { get; set; }
    public int? DiskGb { get; set; }
    public string? DisplayName { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class JobPayload
{
    public string? IdempotencyKey { get; set; }
    public int? CpuCores { get; set; }
    public int? MemoryMb { get; set; }
    public int? DiskGb { get; set; }
    public string? ConfigurationJson { get; set; }
    public string? RequestedVmName { get; set; }
    public string? GameKey { get; set; }
    public string? AccessUsername { get; set; }
    public string? AccessPasswordEncrypted { get; set; }
}

public class EnqueueResult
{
    public long JobId { get; set; }
    public long? InstanceId { get; set; }
    public bool AlreadyExisted { get; set; }
    public string Status { get; set; } = nameof(ProvisioningJobStatus.Queued);

    public static EnqueueResult FromExisting(ProvisioningJob job)
    {
        return new EnqueueResult
        {
            JobId = job.Id,
            InstanceId = job.VirtualGameInstanceId,
            AlreadyExisted = true,
            Status = job.Status.ToString()
        };
    }
}

public class ForceStopResult
{
    public long InstanceId { get; set; }
    public int CancelledJobs { get; set; }
}

public class InstanceListItem
{
    public long Id { get; set; }
    public long OwnerId { get; set; }
    public string? OwnerLogin { get; set; }
    public int VmId { get; set; }
    public string InstanceKind { get; set; } = "VM";
    public int CpuCores { get; set; }
    public int MemoryMb { get; set; }
    public int? DiskGb { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public long GameDefinitionId { get; set; }
    public string GameDisplayName { get; set; } = string.Empty;
    public string? VmDisplayName { get; set; }
    public long? LastJobId { get; set; }
    public string? LastJobStatus { get; set; }
    public string? LastJobStatusLabel { get; set; }
    public string? LastJobOperation { get; set; }
    public string? LastJobOperationLabel { get; set; }
    public string? LastJobErrorMessage { get; set; }
    public bool HasPendingJob { get; set; }
    public string? AccessUsername { get; set; }
    public string? AccessPassword { get; set; }
    public string? PublicHost { get; set; }
    public int? SshPublicPort { get; set; }
    public int? GamePublicPortStart { get; set; }
    public int? GamePublicPortCount { get; set; }
    public string? GamePublicPortProtocol { get; set; }
}

public class ProvisioningJobStatusItem
{
    public long Id { get; set; }
    public long? VirtualGameInstanceId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string OperationLabel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public DateTime? StartedDateUtc { get; set; }
    public DateTime? CompletedDateUtc { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; }
}

public class VmGameRuntimeInfo
{
    public long InstanceId { get; set; }
    public int VmId { get; set; }
    public string Status { get; set; } = "unknown";
    public bool IsRunning { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ConfiguredServerVersion { get; set; }
    public string? DetectedServerVersion { get; set; }
    public string? JavaVersion { get; set; }
    public string? DirectConnectCode { get; set; }
    public int? GamePort { get; set; }
    public bool? IsGamePortListening { get; set; }
    public int? OnlinePlayers { get; set; }
    public int? MaxPlayers { get; set; }
}

public class LiveCommandStreamItem
{
    public long InstanceId { get; set; }
    public int VmId { get; set; }
    public bool Active { get; set; }
    public int? Pid { get; set; }
    public string? CommandSummary { get; set; }
    public int? ExitCode { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string Output { get; set; } = string.Empty;
    public string? Message { get; set; }
    public List<LiveCommandStreamEntry> RecentItems { get; set; } = [];
}

public class LiveCommandStreamEntry
{
    public int VmId { get; set; }
    public int? Pid { get; set; }
    public string? CommandSummary { get; set; }
    public bool Active { get; set; }
    public int? ExitCode { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string Output { get; set; } = string.Empty;
}

public class SshSessionAccess
{
    public long InstanceId { get; set; }
    public int VmId { get; set; }
    public string Host { get; set; } = string.Empty;
    public List<string> CandidateHosts { get; set; } = [];
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class SshTerminalPreviewItem
{
    public long InstanceId { get; set; }
    public bool Available { get; set; }
    public string Source { get; set; } = "ssh";
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public string Output { get; set; } = string.Empty;
    public string? Message { get; set; }
}
