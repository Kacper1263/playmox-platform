using System.ComponentModel.DataAnnotations.Schema;

namespace PlaymoxAPI.BLL.BO.Users
{
    [Table("UsersRoles")]
    public class UserRole : BaseEntity<long>
    {
        [ForeignKey(nameof(User))]
        public long UserId { get; set; }
        public User User { get; set; }
        
        [ForeignKey(nameof(Role))]
        public long RoleId { get; set; }
        public Role Role { get; set; }
    }
}