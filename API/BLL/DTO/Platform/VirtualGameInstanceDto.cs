using PlaymoxAPI.BLL.DTO;

namespace PlaymoxAPI.BLL.DTO.Platform;

public class VirtualGameInstanceDto : BaseEntityDto<long?>
{
    public long OwnerId { get; set; }
    public long GameDefinitionId { get; set; }
    public int VmId { get; set; }
    public string? VmDisplayName { get; set; }
    public string InstanceKind { get; set; }
    public int CpuCores { get; set; }
    public int MemoryMb { get; set; }
    public int? DiskGb { get; set; }
    public string Status { get; set; }
    public string? InternalIp { get; set; }
    public string? PublicHost { get; set; }
    public int? PublicPort { get; set; }
    public int? SshPublicPort { get; set; }
    public int? GamePublicPortStart { get; set; }
    public int? GamePublicPortCount { get; set; }
    public string? GamePublicPortProtocol { get; set; }
    public string ConfigurationJson { get; set; } = "{}";
}
