using System.Security.Claims;
using PlaymoxAPI.BLL.BO.Users;
using PlaymoxAPI.Extensions.Auth;

namespace PlaymoxAPI.Helpers;

public class UserHelper
{
    private IHttpContextAccessor _httpContextAccessor;
    private ITokenService _tokenService;

    public UserHelper(IHttpContextAccessor httpContextAccessor, ITokenService tokenService)
    {
        _httpContextAccessor = httpContextAccessor;
        _tokenService = tokenService;
    }

    public User? GetUser()
    {
        if(_httpContextAccessor.HttpContext == null)
        {
            return null;
        }

        var user = _httpContextAccessor.HttpContext.User;

        var id = user.Claims.SingleOrDefault(p => p.Type == ClaimTypes.Sid)?.Value;
        var login = user.Claims.SingleOrDefault(p => p.Type == ClaimTypes.Name)?.Value;
            
        var result = new User() { Login = login, Id = long.Parse(id ?? "0") };

        if (long.TryParse(id, out var idLong))
        {
            result.UserRoles = Config.GetUserRoleWithCache(idLong);
        }

        return result;
    }
}
