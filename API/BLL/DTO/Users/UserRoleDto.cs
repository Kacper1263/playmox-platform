using System.ComponentModel.DataAnnotations.Schema;

namespace PlaymoxAPI.BLL.DTO.Users
{
    [Table("UsersRoles")]
    public class UserRoleDto : BaseEntityDto<long?>
    {
        public long UserId { get; set; }
        public UserDto User { get; set; }
        
        public long RoleId { get; set; }
        public RoleDto Role { get; set; }
    }
}