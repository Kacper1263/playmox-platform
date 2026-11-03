using PlaymoxAPI.BLL.DTO.Platform;

namespace PlaymoxAPI.BLL.DTO.Users;

public class UserDto : BaseEntityDto<long?>
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Login { get; set; }
    public string Password { get; set; }
    
    public List<UserRoleDto>? UserRoles { get; set; }
    public UserQuotaDto? UserQuota { get; set; }
}
