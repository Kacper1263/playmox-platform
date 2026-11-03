using PlaymoxAPI.BLL.BO.Platform;

namespace PlaymoxAPI.Services.Platform;

public class GameServerConfigurationSnapshot
{
    public long InstanceId { get; set; }
    public string Source { get; set; } = "db";
    public string ConfigurationJson { get; set; } = "{}";
}

public class GameServerBackupArchive
{
    public string FileName { get; set; } = "backup.tar.gz";
    public string ContentType { get; set; } = "application/gzip";
    public byte[] Content { get; set; } = [];
}

public interface IGameServerConnector
{
    GameServerKind Kind { get; }
    Task InstallOrUpdateAsync(
        VirtualGameInstance instance,
        string? configurationJson,
        GameDefinition gameDefinition,
        bool forceReinstall,
        CancellationToken cancellationToken);
    Task StopForShutdownAsync(VirtualGameInstance instance, CancellationToken cancellationToken);
    Task RestartAfterVmResourceChangeAsync(VirtualGameInstance instance, CancellationToken cancellationToken);
    Task<VmGameRuntimeInfo> GetRuntimeInfoAsync(VirtualGameInstance instance, CancellationToken cancellationToken);
    Task<GameServerConfigurationSnapshot> GetConfigurationSnapshotAsync(
        VirtualGameInstance instance,
        string? storedConfigurationJson,
        bool refreshFromServer,
        CancellationToken cancellationToken);
    Task<GameServerBackupArchive?> CreateBackupAsync(VirtualGameInstance instance, CancellationToken cancellationToken);
    Task RestoreBackupAsync(
        VirtualGameInstance instance,
        string archiveFilePath,
        long archiveSizeBytes,
        CancellationToken cancellationToken);
}
