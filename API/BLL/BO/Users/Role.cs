using System.ComponentModel.DataAnnotations.Schema;

namespace PlaymoxAPI.BLL.BO.Users
{
    [Table("Roles")]
    public class Role : BaseEntity<long>
    {
        public string Name { get; set; }
        
        public List<RolePermission> Permissions { get; set; }
    }
}