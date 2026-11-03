namespace PlaymoxAPI.BLL.DTO.Users;

public class UserTokenDto : BaseEntityDto<long?>
{
    public long UserId { get; set; }
    public string RefreshToken { get; set; }
    public DateTime? RefreshTokenGenerateDate { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }
}