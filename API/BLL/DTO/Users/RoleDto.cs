using System.ComponentModel.DataAnnotations.Schema;

namespace PlaymoxAPI.BLL.DTO.Users
{
    [Table("Roles")]
    public class RoleDto : BaseEntityDto<long?>
    {
        public string Name { get; set; }
        
        public List<RolePermissionDto> Permissions { get; set; }
    }
}