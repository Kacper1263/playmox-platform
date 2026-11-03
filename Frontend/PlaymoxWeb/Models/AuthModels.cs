namespace PlaymoxWeb.Models;

public class LoginRequest
{
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class TokenApiModel
{
    public UserDto? UserBO { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}

public class UserDto
{
    public long? Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public List<UserRoleDto>? UserRoles { get; set; }
    public UserQuotaDto? UserQuota { get; set; }
}

public class UserRoleDto
{
    public long RoleId { get; set; }
    public RoleDto? Role { get; set; }
}

public class RoleDto
{
    public string Name { get; set; } = string.Empty;
}

public class UserQuotaDto
{
    public long? Id { get; set; }
    public long UserId { get; set; }
    public int CpuCoresLimit { get; set; }
    public int MemoryMbLimit { get; set; }
    public int MaxInstancesLimit { get; set; }
    public int? DiskGbLimit { get; set; }
    public int? MaxPublicPortsLimit { get; set; }
    public bool AllowSshPublicPort { get; set; }
}

public class ProxmoxSettingsDto
{
    public long? Id { get; set; }
    public string ProxmoxUrl { get; set; } = string.Empty;
    public string ProxmoxNode { get; set; } = string.Empty;
    public string ApiUser { get; set; } = string.Empty;
    public string ApiTokenId { get; set; } = string.Empty;
    public string ApiTokenSecret { get; set; } = string.Empty;
    public bool HasApiTokenSecret { get; set; }
    public int TemplateVmid { get; set; }
    public string Storage { get; set; } = string.Empty;
    public string Bridge { get; set; } = string.Empty;
    public string ExternalAddress { get; set; } = string.Empty;
}

public class AdminProxmoxDashboardDto
{
    public bool IsConfigured { get; set; }
    public bool IsConnected { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ProxmoxUrl { get; set; } = string.Empty;
    public string ProxmoxNode { get; set; } = string.Empty;
    public string ProxmoxVersion { get; set; } = string.Empty;
    public double? CpuUsagePercent { get; set; }
    public int? CpuCores { get; set; }
    public long? MemoryUsedBytes { get; set; }
    public long? MemoryTotalBytes { get; set; }
    public long? RootFsUsedBytes { get; set; }
    public long? RootFsTotalBytes { get; set; }
    public int VmCount { get; set; }
    public int TemplateCount { get; set; }
    public int CtCount { get; set; }
    public List<AdminProxmoxStorageDto> Storages { get; set; } = [];
}

public class AdminProxmoxStorageDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Active { get; set; }
    public long? UsedBytes { get; set; }
    public long? TotalBytes { get; set; }
}

public class GameDefinitionDto
{
    public long? Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string LinuxGsmServerId { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int? DefaultPort { get; set; }
    public int RequiredPublicPorts { get; set; }
    public string JsonSchema { get; set; } = "{}";
}

public class CreateVmRequest
{
    public long GameDefinitionId { get; set; }
    public string InstanceKind { get; set; } = "VM";
    public string? DisplayName { get; set; }
    public int CpuCores { get; set; } = 2;
    public int MemoryMb { get; set; } = 2048;
    public int? DiskGb { get; set; } = 20;
    public string ConfigurationJson { get; set; } = "{}";
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

public class ResetPasswordRequest
{
    public string? IdempotencyKey { get; set; }
    public string? Password { get; set; }
}

public class JobAcceptedResponse
{
    public long JobId { get; set; }
    public long? VirtualGameInstanceId { get; set; }
    public string Status { get; set; } = "Queued";
    public bool AlreadyExisted { get; set; }
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

public class SshTerminalPreviewItem
{
    public long InstanceId { get; set; }
    public bool Available { get; set; }
    public string Source { get; set; } = "ssh";
    public DateTime GeneratedAtUtc { get; set; }
    public string Output { get; set; } = string.Empty;
    public string? Message { get; set; }
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

public class GameServerConfigurationSnapshot
{
    public long InstanceId { get; set; }
    public string Source { get; set; } = "db";
    public string ConfigurationJson { get; set; } = "{}";
}

public class UserPlatformSettingsDto
{
    public string? SteamUsername { get; set; }
    public bool HasSteamPassword { get; set; }
}

public class UpdateUserPlatformSettingsRequest
{
    public string? SteamUsername { get; set; }
    public string? SteamPassword { get; set; }
    public bool ClearSteamPassword { get; set; }
}
