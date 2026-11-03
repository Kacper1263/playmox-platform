using PlaymoxAPI.BLL.DTO;

namespace PlaymoxAPI.BLL.DTO.Platform;

public class GameDefinitionDto : BaseEntityDto<long?>
{
    public string Key { get; set; }
    public string DisplayName { get; set; }
    public string LinuxGsmServerId { get; set; }
    public bool IsActive { get; set; }
    public int? DefaultPort { get; set; }
    public int RequiredPublicPorts { get; set; }
    public string JsonSchema { get; set; }
}
