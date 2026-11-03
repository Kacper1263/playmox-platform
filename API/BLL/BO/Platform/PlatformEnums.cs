using System.ComponentModel;

namespace PlaymoxAPI.BLL.BO.Platform;

public enum ProvisioningOperation
{
    [Description("Create VM")] CreateVm = 1,
    [Description("Start VM")] StartVm = 2,
    [Description("Stop VM")] StopVm = 3,
    [Description("Restart VM")] RestartVm = 4,
    [Description("Delete VM")] DeleteVm = 5,
    [Description("Update VM Resources")] UpdateVmResources = 6,
    [Description("Reset VM Password")] ResetVmPassword = 7,
    [Description("Shutdown VM")] ShutdownVm = 8,
    [Description("Install Game Server")] InstallGameServer = 9,
    [Description("Reinstall Game Server")] ReinstallGameServer = 10,
    [Description("Update Game Configuration")] UpdateGameConfiguration = 11
}

public enum ProvisioningJobStatus
{
    [Description("Queued")] Queued = 1,
    [Description("Running")] Running = 2,
    [Description("Succeeded")] Succeeded = 3,
    [Description("Failed")] Failed = 4
}

public enum VirtualInstanceStatus
{
    [Description("Creating")] Creating = 1,
    [Description("Running")] Running = 2,
    [Description("Stopped")] Stopped = 3,
    [Description("Updating")] Updating = 4,
    [Description("Deleted")] Deleted = 5,
    [Description("Error")] Error = 6
}

public enum VirtualInstanceKind
{
    VM = 1,
    CT = 2
}
