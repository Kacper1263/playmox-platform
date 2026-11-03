using System.ComponentModel.DataAnnotations.Schema;

namespace PlaymoxAPI.BLL.BO.Users
{
    [Table("RolesPermissions")]
    public class RolePermission : BaseEntity<long>
    {
        [ForeignKey(nameof(Role))]
        public long RoleId { get; set; }
        public Role Role { get; set; }
        
        public string PermissionId { get; set; }
    }
}