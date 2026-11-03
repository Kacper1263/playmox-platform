using Microsoft.EntityFrameworkCore;
using PlaymoxAPI.BLL.BO.Platform;

namespace PlaymoxAPI.BLL.BO.Users;

[Index(nameof(Login), IsUnique = true)]
public class User : BaseEntity<long>
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Login { get; set; }
    public string PasswordHash { get; set; }
    public string? SteamUsername { get; set; }
    public string? SteamPasswordEncrypted { get; set; }
    
    public List<UserRole> UserRoles { get; set; }
    public UserQuota UserQuota { get; set; }
    public List<VirtualGameInstance> VirtualGameInstances { get; set; }
    public List<ProvisioningJob> ProvisioningJobs { get; set; }
}
