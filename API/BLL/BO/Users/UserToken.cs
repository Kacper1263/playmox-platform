namespace PlaymoxAPI.BLL.BO.Users;

public class UserToken : BaseEntity<long>
{
    public long UserId { get; set; }
    public string RefreshToken { get; set; }
    public DateTime? RefreshTokenGenerateDate { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }
}