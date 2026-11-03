using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PlaymoxAPI.BLL.DAL.Users;
using PlaymoxAPI.BLL.DTO.Users;
using PlaymoxAPI.Extensions.Auth;
using PlaymoxAPI.Extensions.Mapper;
using PlaymoxAPI.Helpers;

namespace PlaymoxAPI.Controllers.Auth;

[Route("api/auth")]
[ApiController]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private const int RefreshTokenValidityInHours = 24 * 31;
    private readonly UsersRepository usersRepo = new UsersRepository();
    private readonly ITokenService tokenService;
    private readonly IHttpContextAccessor httpContextAccessor;

    public AuthController(ITokenService tokenService, IHttpContextAccessor httpContextAccessor)
    {
        this.httpContextAccessor = httpContextAccessor;
        this.tokenService = tokenService;
    }

    [HttpGet, Route("get-current-user")]
    public IActionResult GetCurrentUser()
    {
        var user = new UserHelper(httpContextAccessor, tokenService).GetUser();
        if (user == null)
        {
            return Unauthorized("User not authenticated");
        }
        return Ok(new CurrentUserModel
        {
            UserBO = Config.Mapper.Map<UserDto>(user)
        });
    }

    [HttpPost, Route("login")]
    [EnableRateLimiting("auth")]
    public IActionResult Login([FromBody] LoginApiModel loginModel)
    {
        if (loginModel == null)
        {
            return BadRequest("Invalid client request");
        }

        List<Claim> claims;
        string accessToken;
        string refreshToken;
        ClaimsIdentity claimsIdentity;

        var userBO = usersRepo.Login(loginModel.Login, loginModel.Password);

        if (userBO == null)
        {
            return Unauthorized();
        }

        claims = new List<Claim>
        {
            new Claim(ClaimTypes.Sid, userBO.Id.ToString()),
            new Claim(ClaimTypes.Name, userBO.Login),
            new Claim(ClaimTypes.Actor, "Default")
        };

        foreach (var role in userBO.UserRoles.Where(x => x.Role?.Name != null))
        {
            claims.Add(new Claim(ClaimTypes.Role, role.Role.Name));
        }

        accessToken = tokenService.GenerateAccessToken(claims);
        refreshToken = tokenService.GenerateRefreshToken();

        usersRepo.UpdateRefreshToken(userBO.Id, null, refreshToken, DateTime.Now.AddHours(RefreshTokenValidityInHours));

        claimsIdentity = new ClaimsIdentity(claims);

        User.AddIdentity(claimsIdentity);

        return Ok(new TokenApiModel
        {
            UserBO = userBO.To<UserDto>(),
            AccessToken = accessToken,
            RefreshToken = refreshToken
        });
    }

    [HttpPost]
    [Route("refresh")]
    [EnableRateLimiting("auth")]
    public IActionResult Refresh([FromBody] TokenApiModel tokenApiModel)
    {
        if (tokenApiModel is null)
        {
            return BadRequest("Invalid client request");
        }

        string accessToken = tokenApiModel.AccessToken;
        string refreshToken = tokenApiModel.RefreshToken;

        var principal = tokenService.GetPrincipalFromExpiredToken(accessToken);

        var login = principal.Identity?.Name ?? throw new Exception("Username not found in token");
        var newRefreshToken = tokenService.GenerateRefreshToken();

        var userBO = usersRepo.Q
            .Include(x => x.UserRoles).ThenInclude(x => x.Role).ThenInclude(x => x.Permissions)
            .SingleOrDefault(u => u.Login == login);
        if (userBO == null)
        {
            return BadRequest($"User with login {login} not found in database");
        }

        var userRequestedToken = usersRepo.DC.UsersTokens
            .SingleOrDefault(x => x.UserId == userBO.Id && x.RefreshToken == refreshToken);

        if (userRequestedToken == null)
        {
            return BadRequest("Refresh token is invalid");
        }
        if (userRequestedToken.RefreshTokenExpiryTime <= DateTime.Now)
        {
            return BadRequest("Refresh token has expired");
        }

        usersRepo.UpdateRefreshToken(userBO.Id, refreshToken, newRefreshToken, DateTime.Now.AddHours(RefreshTokenValidityInHours));

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Sid, userBO.Id.ToString()),
            new Claim(ClaimTypes.Name, userBO.Login),
            new Claim(ClaimTypes.Actor, "Default")
        };
        foreach (var role in userBO.UserRoles.Where(x => x.Role?.Name != null))
        {
            claims.Add(new Claim(ClaimTypes.Role, role.Role!.Name));
        }

        var newAccessToken = tokenService.GenerateAccessToken(claims);

        return Ok(new TokenApiModel
        {
            UserBO = userBO.To<UserDto>(),
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken
        });
    }

    [HttpPost, Authorize]
    [Route("revoke")]
    public IActionResult Revoke([FromBody] RevokeTokenRequest? request)
    {
        var login = User?.Identity?.Name;

        if (string.IsNullOrWhiteSpace(login))
        {
            return Unauthorized("User not authenticated");
        }

        var user = usersRepo.Q.SingleOrDefault(u => u.Login == login);
        if (user == null)
        {
            return BadRequest("User not found");
        }

        if (!string.IsNullOrWhiteSpace(request?.RefreshToken))
        {
            usersRepo.RevokeRefreshToken(user.Id, request.RefreshToken);
        }
        else
        {
            usersRepo.RevokeAllRefreshTokens(user.Id);
        }

        return NoContent();
    }
}

public class LoginApiModel
{
    public string Login { get; set; }
    public string Password { get; set; }
}

public class TokenApiModel
{
    public UserDto? UserBO { get; set; }
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
}

public class CurrentUserModel
{
    public UserDto UserBO { get; set; }
}

public class RevokeTokenRequest
{
    public string? RefreshToken { get; set; }
}
