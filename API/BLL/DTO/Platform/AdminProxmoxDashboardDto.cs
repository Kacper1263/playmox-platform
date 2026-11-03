namespace PlaymoxAPI.BLL.DTO.Platform;

public class AdminProxmoxDashboardDto
{
    public bool IsConfigured { get; set; }
    public bool IsConnected { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ProxmoxUrl { get; set; } = string.Empty;
    public string ProxmoxNode { get; set; } = string.Empty;
    public string ProxmoxVersion { get; set; } = string.Empty;
    public double? CpuUsagePercent { get; set; }
    public int? CpuCores { get; set; }
    public long? MemoryUsedBytes { get; set; }
    public long? MemoryTotalBytes { get; set; }
    public long? RootFsUsedBytes { get; set; }
    public long? RootFsTotalBytes { get; set; }
    public int VmCount { get; set; }
    public int TemplateCount { get; set; }
    public int CtCount { get; set; }
    public List<AdminProxmoxStorageDto> Storages { get; set; } = [];
}

public class AdminProxmoxStorageDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Active { get; set; }
    public long? UsedBytes { get; set; }
    public long? TotalBytes { get; set; }
}
