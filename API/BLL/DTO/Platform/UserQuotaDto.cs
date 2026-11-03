using PlaymoxAPI.BLL.DTO;

namespace PlaymoxAPI.BLL.DTO.Platform;

public class UserQuotaDto : BaseEntityDto<long?>
{
    public long UserId { get; set; }
    public int CpuCoresLimit { get; set; }
    public int MemoryMbLimit { get; set; }
    public int MaxInstancesLimit { get; set; }
    public int? DiskGbLimit { get; set; }
    public int? MaxPublicPortsLimit { get; set; }
    public bool AllowSshPublicPort { get; set; }
}
