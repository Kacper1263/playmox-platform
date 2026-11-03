namespace PlaymoxAPI.BLL.BO;

public interface IEntity
{
    string CreatedUser { get; set; }
    DateTime CreatedDate { get; set; }
    string ModifiedUser { get; set; }
    DateTime? ModifiedDate { get; set; }
    string Extra { get; }
}