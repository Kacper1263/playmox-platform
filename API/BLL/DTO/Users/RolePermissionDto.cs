using System.ComponentModel.DataAnnotations.Schema;

namespace PlaymoxAPI.BLL.DTO.Users
{
    [Table("RolesPermissions")]
    public class RolePermissionDto : BaseEntityDto<long?>
    {
        public long RoleId { get; set; }
        public RoleDto Role { get; set; }
        
        public string PermissionId { get; set; }
    }
}