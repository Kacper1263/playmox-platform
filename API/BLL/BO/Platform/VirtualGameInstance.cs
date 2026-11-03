using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using PlaymoxAPI.BLL.BO.Users;

namespace PlaymoxAPI.BLL.BO.Platform;

[Table("VirtualGameInstances")]
[Index(nameof(VmId), IsUnique = true)]
[Index(nameof(OwnerId), nameof(Status))]
public class VirtualGameInstance : BaseEntity<long>
{
    [ForeignKey(nameof(Owner))]
    public long OwnerId { get; set; }
    public User Owner { get; set; }

    [ForeignKey(nameof(GameDefinition))]
    public long GameDefinitionId { get; set; }
    public GameDefinition GameDefinition { get; set; }

    public int VmId { get; set; }
    public string? VmDisplayName { get; set; }
    public VirtualInstanceKind InstanceKind { get; set; }
    public int CpuCores { get; set; }
    public int MemoryMb { get; set; }
    public int? DiskGb { get; set; }
    public VirtualInstanceStatus Status { get; set; }
    public string? InternalIp { get; set; }
    public string? PublicHost { get; set; }
    public int? PublicPort { get; set; }
    public int? SshPublicPort { get; set; }
    public int? GamePublicPortStart { get; set; }
    public int? GamePublicPortCount { get; set; }
    public string? GamePublicPortProtocol { get; set; }

    [Column(TypeName = VarcharMaxColumnTypeName)]
    public string ConfigurationJson { get; set; } = "{}";

    public List<ProvisioningJob> ProvisioningJobs { get; set; }
    public List<PortLease> PortLeases { get; set; }
}
