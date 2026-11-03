using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using PlaymoxAPI.BLL.BO.Users;

namespace PlaymoxAPI.BLL.BO.Platform;

[Table("UsersQuotas")]
[Index(nameof(UserId), IsUnique = true)]
public class UserQuota : BaseEntity<long>
{
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }
    public User User { get; set; }

    public int CpuCoresLimit { get; set; }
    public int MemoryMbLimit { get; set; }
    public int MaxInstancesLimit { get; set; }
    public int? DiskGbLimit { get; set; }
    public int? MaxPublicPortsLimit { get; set; }
    public bool AllowSshPublicPort { get; set; }
}
