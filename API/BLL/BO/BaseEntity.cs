using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PlaymoxAPI.BLL.BO;

public abstract class BaseEntity<T> : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public virtual T Id { get; set; }
    
    public const string VarcharMaxColumnTypeName = "varchar(MAX)";

    public virtual string CreatedUser { get; set; }
    public virtual DateTime CreatedDate { get; set; }
    public virtual string ModifiedUser { get; set; }
    public virtual DateTime? ModifiedDate { get; set; }
    
    [Column(TypeName = VarcharMaxColumnTypeName)]
    public string? Extra { get; set; }
}