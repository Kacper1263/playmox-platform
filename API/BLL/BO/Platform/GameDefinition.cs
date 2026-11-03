using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PlaymoxAPI.BLL.BO.Platform;

[Table("GameDefinitions")]
[Index(nameof(Key), IsUnique = true)]
public class GameDefinition : BaseEntity<long>
{
    public string Key { get; set; }
    public string DisplayName { get; set; }
    public string LinuxGsmServerId { get; set; }
    public bool IsActive { get; set; } = true;
    public int? DefaultPort { get; set; }
    public int RequiredPublicPorts { get; set; }

    [Column(TypeName = VarcharMaxColumnTypeName)]
    public string JsonSchema { get; set; }

    public List<VirtualGameInstance> VirtualGameInstances { get; set; }
}
