using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PlaymoxAPI.BLL.BO.Platform;

[Table("PortLeases")]
[Index(nameof(Port), IsUnique = true)]
[Index(nameof(VirtualGameInstanceId))]
public class PortLease : BaseEntity<long>
{
    [ForeignKey(nameof(VirtualGameInstance))]
    public long VirtualGameInstanceId { get; set; }
    public VirtualGameInstance VirtualGameInstance { get; set; }

    public int Port { get; set; }
}
