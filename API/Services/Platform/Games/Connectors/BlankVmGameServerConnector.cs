using PlaymoxAPI.BLL.BO.Platform;

namespace PlaymoxAPI.Services.Platform;

public sealed class BlankVmGameServerConnector : IGameServerConnector
{
    public GameServerKind Kind => GameServerKind.BlankVm;

    public Task InstallOrUpdateAsync(
        VirtualGameInstance instance,
        string? configurationJson,
        GameDefinition gameDefinition,
        bool forceReinstall,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task StopForShutdownAsync(VirtualGameInstance instance, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task RestartAfterVmResourceChangeAsync(VirtualGameInstance instance, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<VmGameRuntimeInfo> GetRuntimeInfoAsync(VirtualGameInstance instance, CancellationToken cancellationToken)
    {
        return Task.FromResult(new VmGameRuntimeInfo
        {
            InstanceId = instance.Id,
            VmId = instance.VmId,
            Status = "N/A",
            IsRunning = false,
            Message = "This instance does not have a game server."
        });
    }

    public Task<GameServerConfigurationSnapshot> GetConfigurationSnapshotAsync(
        VirtualGameInstance instance,
        string? storedConfigurationJson,
        bool refreshFromServer,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new GameServerConfigurationSnapshot
        {
            InstanceId = instance.Id,
            Source = "db",
            ConfigurationJson = "{}"
        });
    }

    public Task<GameServerBackupArchive?> CreateBackupAsync(VirtualGameInstance instance, CancellationToken cancellationToken)
        => Task.FromResult<GameServerBackupArchive?>(null);

    public Task RestoreBackupAsync(
        VirtualGameInstance instance,
        string archiveFilePath,
        long archiveSizeBytes,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}
