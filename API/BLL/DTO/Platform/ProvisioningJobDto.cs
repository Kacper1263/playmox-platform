using PlaymoxAPI.BLL.DTO;

namespace PlaymoxAPI.BLL.DTO.Platform;

public class ProvisioningJobDto : BaseEntityDto<long?>
{
    public long RequestedByUserId { get; set; }
    public long? VirtualGameInstanceId { get; set; }
    public string Operation { get; set; }
    public string Status { get; set; }
    public DateTime? StartedDateUtc { get; set; }
    public DateTime? CompletedDateUtc { get; set; }
    public string? PayloadJson { get; set; }
    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
}
