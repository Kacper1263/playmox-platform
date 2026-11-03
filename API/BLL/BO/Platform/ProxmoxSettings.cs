namespace PlaymoxAPI.BLL.BO.Platform;

public class ProxmoxSettings : BaseEntity<long>
{
    public string ProxmoxUrl { get; set; } = string.Empty;
    public string ProxmoxNode { get; set; } = string.Empty;
    public string ApiUser { get; set; } = string.Empty;
    public string ApiTokenId { get; set; } = string.Empty;
    public string ApiTokenSecret { get; set; } = string.Empty;
    public int TemplateVmid { get; set; }
    public string Storage { get; set; } = string.Empty;
    public string Bridge { get; set; } = string.Empty;
    public string ExternalAddress { get; set; } = string.Empty;
}
