using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using PlaymoxAPI.BLL.BO.Users;

namespace PlaymoxAPI.BLL.BO.Platform;

[Table("ProvisioningJobs")]
[Index(nameof(Status), nameof(CreatedDate))]
[Index(nameof(VirtualGameInstanceId), nameof(Status))]
public class ProvisioningJob : BaseEntity<long>
{
    [ForeignKey(nameof(RequestedByUser))]
    public long RequestedByUserId { get; set; }
    public User RequestedByUser { get; set; }

    [ForeignKey(nameof(VirtualGameInstance))]
    public long? VirtualGameInstanceId { get; set; }
    public VirtualGameInstance? VirtualGameInstance { get; set; }

    public ProvisioningOperation Operation { get; set; }
    public ProvisioningJobStatus Status { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public DateTime? StartedDateUtc { get; set; }
    public DateTime? CompletedDateUtc { get; set; }

    [Column(TypeName = VarcharMaxColumnTypeName)]
    public string? PayloadJson { get; set; }

    [Column(TypeName = VarcharMaxColumnTypeName)]
    public string? ResultJson { get; set; }

    [Column(TypeName = VarcharMaxColumnTypeName)]
    public string? ErrorMessage { get; set; }
}
