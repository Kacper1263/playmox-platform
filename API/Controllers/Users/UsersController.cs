using System.Security.Claims;
using PlaymoxAPI.BLL;
using PlaymoxAPI.BLL.BO.Platform;
using PlaymoxAPI.BLL.BO.Users;
using PlaymoxAPI.BLL.DAL.Users;
using PlaymoxAPI.BLL.DTO.Platform;
using PlaymoxAPI.BLL.DTO.Users;
using PlaymoxAPI.Extensions.Auth;
using PlaymoxAPI.Helpers;
using PlaymoxAPI.Services.Proxmox;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PlaymoxAPI.Controllers.Users;

[BaseAuth]
[ApiController]
[Route("api/[controller]")]
public class UsersController : Controller
{
    private readonly ProxmoxAutomationClient proxmoxAutomationClient;

    public UsersController(ProxmoxAutomationClient proxmoxAutomationClient)
    {
        this.proxmoxAutomationClient = proxmoxAutomationClient;
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public ActionResult<List<UserDto>> GetUsers()
    {
        var users = new UsersRepository().Q
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .Include(x => x.UserQuota)
            .ToList();
        return Ok(users.Select(x => Config.Mapper.Map<UserDto>(x)));
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    [Route("{id}")]
    public IActionResult GetUserById(int id)
    {
        var user = new UsersRepository().Q
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .Include(x => x.UserQuota)
            .FirstOrDefault(x => x.Id == id);
        if (user == null)
        {
            return NotFound();
        }
        return Ok(Config.Mapper.Map<UserDto>(user));
    }
    
    [Authorize(Roles = "Admin")]
    [HttpPost]
    [Route("create")]
    public async Task<IActionResult> CreateUser([FromBody] UserDto? userDto)
    {
        if (userDto == null)
        {
            return BadRequest("User data is required.");
        }
        
        if (string.IsNullOrEmpty(userDto.FirstName) || string.IsNullOrEmpty(userDto.Login))
        {
            return BadRequest("First name and login are required.");
        }

        if (string.IsNullOrWhiteSpace(userDto.Password))
        {
            return BadRequest("Password is required.");
        }

        if (userDto.UserQuota == null)
        {
            return BadRequest("User quota is required.");
        }

        if (userDto.UserQuota.CpuCoresLimit < 1 || userDto.UserQuota.MemoryMbLimit < 256 || userDto.UserQuota.MaxInstancesLimit < 1)
        {
            return BadRequest("Invalid quota values.");
        }

        var cpuLimitValidation = await ValidateCpuQuotaUpperBoundAsync(userDto.UserQuota.CpuCoresLimit);
        if (cpuLimitValidation != null)
        {
            return BadRequest(cpuLimitValidation);
        }
        
        // check if user already exists
        var existingUser = new UsersRepository().Q.FirstOrDefault(x => x.Login == userDto.Login);
        if (existingUser != null)
        {
            return Conflict("User with this login already exists.");
        }
        
        var hash = Crypto.HashPassword(userDto.Password);

        var user = new User
        {
            FirstName = userDto.FirstName,
            LastName = userDto.LastName,
            Login = userDto.Login,
            PasswordHash = hash,
        };

        var repo = new UsersRepository();
        var userRole = repo.DC.Roles.FirstOrDefault(x => x.Name == "User");
        if (userRole == null)
        {
            userRole = new Role { Name = "User" };
            repo.DC.Roles.Add(userRole);
            repo.SaveChanges();
        }

        repo.DC.Users.Add(user);
        repo.SaveChanges();

        repo.DC.UsersRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = userRole.Id
        });

        repo.DC.UsersQuotas.Add(new UserQuota
        {
            UserId = user.Id,
            CpuCoresLimit = userDto.UserQuota.CpuCoresLimit,
            MemoryMbLimit = userDto.UserQuota.MemoryMbLimit,
            MaxInstancesLimit = userDto.UserQuota.MaxInstancesLimit,
            DiskGbLimit = userDto.UserQuota.DiskGbLimit,
            MaxPublicPortsLimit = userDto.UserQuota.MaxPublicPortsLimit,
            AllowSshPublicPort = userDto.UserQuota.AllowSshPublicPort
        });
        repo.SaveChanges();
        
        return CreatedAtAction(nameof(GetUserById), new { id = user.Id }, Config.Mapper.Map<UserDto>(user));
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete]
    [Route("{id}")]
    public IActionResult DeleteUser(long id)
    {
        var adminSid = User.Claims.SingleOrDefault(x => x.Type == ClaimTypes.Sid)?.Value;
        if (long.TryParse(adminSid, out var adminId) && adminId == id)
        {
            return BadRequest("Admin cannot delete currently authenticated account.");
        }

        var repo = new UsersRepository();
        var user = repo.DC.Users.FirstOrDefault(x => x.Id == id);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var isTargetAdmin = repo.DC.UsersRoles
            .Include(x => x.Role)
            .Any(x => x.UserId == id && x.Role != null && x.Role.Name == "Admin");
        if (isTargetAdmin)
        {
            return BadRequest("Admin account cannot be deleted.");
        }

        var userTokens = repo.DC.UsersTokens.Where(x => x.UserId == id).ToList();
        if (userTokens.Any())
        {
            repo.DC.UsersTokens.RemoveRange(userTokens);
        }

        var userRoles = repo.DC.UsersRoles.Where(x => x.UserId == id).ToList();
        if (userRoles.Any())
        {
            repo.DC.UsersRoles.RemoveRange(userRoles);
        }

        var userQuota = repo.DC.UsersQuotas.FirstOrDefault(x => x.UserId == id);
        if (userQuota != null)
        {
            repo.DC.UsersQuotas.Remove(userQuota);
        }

        repo.DC.Users.Remove(user);
        repo.SaveChanges();

        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    [Route("{id}/quota")]
    public IActionResult GetUserQuota(long id)
    {
        var repo = new UsersRepository();
        var quota = repo.DC.UsersQuotas.FirstOrDefault(x => x.UserId == id);
        if (quota == null)
        {
            return NotFound("Quota not found for user.");
        }

        return Ok(Config.Mapper.Map<UserQuotaDto>(quota));
    }

    [Authorize]
    [HttpGet]
    [Route("me/quota")]
    public IActionResult GetMyQuota()
    {
        var userSid = User.Claims.SingleOrDefault(x => x.Type == ClaimTypes.Sid)?.Value;
        if (!long.TryParse(userSid, out var userId))
        {
            return Unauthorized("Invalid user identity.");
        }

        var repo = new UsersRepository();
        var quota = repo.DC.UsersQuotas.FirstOrDefault(x => x.UserId == userId);
        if (quota == null)
        {
            return NotFound("Quota not found for current user.");
        }

        return Ok(Config.Mapper.Map<UserQuotaDto>(quota));
    }

    [Authorize]
    [HttpGet]
    [Route("me/platform-settings")]
    public IActionResult GetMyPlatformSettings()
    {
        var userSid = User.Claims.SingleOrDefault(x => x.Type == ClaimTypes.Sid)?.Value;
        if (!long.TryParse(userSid, out var userId))
        {
            return Unauthorized("Invalid user identity.");
        }

        var repo = new UsersRepository();
        var user = repo.Q.FirstOrDefault(x => x.Id == userId);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        return Ok(new UserPlatformSettingsDto
        {
            SteamUsername = user.SteamUsername,
            HasSteamPassword = !string.IsNullOrWhiteSpace(user.SteamPasswordEncrypted)
        });
    }

    [Authorize]
    [HttpPut]
    [Route("me/platform-settings")]
    public IActionResult UpdateMyPlatformSettings([FromBody] UpdateUserPlatformSettingsRequest request)
    {
        if (request == null)
        {
            return BadRequest("Request payload is required.");
        }

        var userSid = User.Claims.SingleOrDefault(x => x.Type == ClaimTypes.Sid)?.Value;
        if (!long.TryParse(userSid, out var userId))
        {
            return Unauthorized("Invalid user identity.");
        }

        var repo = new UsersRepository();
        var user = repo.QTracking.FirstOrDefault(x => x.Id == userId);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var steamUsername = string.IsNullOrWhiteSpace(request.SteamUsername)
            ? null
            : request.SteamUsername.Trim();
        if (!string.IsNullOrWhiteSpace(steamUsername) && steamUsername.Length > 100)
        {
            return BadRequest("Steam username is too long.");
        }

        user.SteamUsername = steamUsername;
        if (request.ClearSteamPassword)
        {
            user.SteamPasswordEncrypted = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.SteamPassword))
        {
            user.SteamPasswordEncrypted = SecureSecret.Encrypt(request.SteamPassword.Trim());
        }

        repo.SaveChanges();
        return Ok(new UserPlatformSettingsDto
        {
            SteamUsername = user.SteamUsername,
            HasSteamPassword = !string.IsNullOrWhiteSpace(user.SteamPasswordEncrypted)
        });
    }

    [Authorize(Roles = "Admin")]
    [HttpPut]
    [Route("{id}/quota")]
    public async Task<IActionResult> UpsertUserQuota(long id, [FromBody] UpsertUserQuotaApiModel model)
    {
        if (model.CpuCoresLimit < 1 || model.MemoryMbLimit < 256 || model.MaxInstancesLimit < 1)
        {
            return BadRequest("Invalid quota values.");
        }

        var cpuLimitValidation = await ValidateCpuQuotaUpperBoundAsync(model.CpuCoresLimit);
        if (cpuLimitValidation != null)
        {
            return BadRequest(cpuLimitValidation);
        }

        var repo = new UsersRepository();
        var userExists = repo.Q.Any(x => x.Id == id);
        if (!userExists)
        {
            return NotFound("User not found.");
        }

        var quota = repo.DC.UsersQuotas.FirstOrDefault(x => x.UserId == id);
        if (quota == null)
        {
            quota = new UserQuota
            {
                UserId = id
            };
            repo.DC.UsersQuotas.Add(quota);
        }

        quota.CpuCoresLimit = model.CpuCoresLimit;
        quota.MemoryMbLimit = model.MemoryMbLimit;
        quota.MaxInstancesLimit = model.MaxInstancesLimit;
        quota.DiskGbLimit = model.DiskGbLimit;
        quota.MaxPublicPortsLimit = model.MaxPublicPortsLimit;
        quota.AllowSshPublicPort = model.AllowSshPublicPort;
        repo.SaveChanges();

        return Ok(Config.Mapper.Map<UserQuotaDto>(quota));
    }

    private async Task<string?> ValidateCpuQuotaUpperBoundAsync(int requestedCpuLimit)
    {
        using var dbContext = new PlaymoxContext();
        var proxmoxConfigured = await dbContext.ProxmoxSettings
            .AsNoTracking()
            .AnyAsync();
        if (!proxmoxConfigured)
        {
            return null;
        }

        var hostCpuCores = await proxmoxAutomationClient.GetNodeCpuCoreCountAsync();
        if (!hostCpuCores.HasValue)
        {
            return "Cannot validate CPU quota against Proxmox host cores.";
        }

        if (requestedCpuLimit > hostCpuCores.Value)
        {
            return $"CPU quota cannot be higher than available host cores ({hostCpuCores.Value}).";
        }

        return null;
    }
}

public class UpsertUserQuotaApiModel
{
    public int CpuCoresLimit { get; set; }
    public int MemoryMbLimit { get; set; }
    public int MaxInstancesLimit { get; set; }
    public int? DiskGbLimit { get; set; }
    public int? MaxPublicPortsLimit { get; set; }
    public bool AllowSshPublicPort { get; set; }
}

public class UserPlatformSettingsDto
{
    public string? SteamUsername { get; set; }
    public bool HasSteamPassword { get; set; }
}

public class UpdateUserPlatformSettingsRequest
{
    public string? SteamUsername { get; set; }
    public string? SteamPassword { get; set; }
    public bool ClearSteamPassword { get; set; }
}
