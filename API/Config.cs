using AutoMapper;
using PlaymoxAPI.BLL.BO.Users;
using PlaymoxAPI.BLL.DAL.Users;
using PlaymoxAPI.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace PlaymoxAPI;

public static class Config
{
    public static string Secret { get; set; } = string.Empty;
    public static string PasswordSalt { get; set; }
    public static string ConnectionString { get; set; }
    public static bool AllowSelfSignedProxmoxCertificates { get; set; } = true;
    public static IMapper Mapper { get; set; }
    public static UserHelper UserHelper { get; set; }

    private static MemoryCache userRolesCache;

    public static void Init()
    {
        userRolesCache = new MemoryCache(new MemoryCacheOptions());
    }

    public static List<UserRole> GetUserRoleWithCache(long userId, double cacheExpiryTimeInHours = 24, bool skipCache = false)
    {
        var cacheKey = $"{userId}";

        List<UserRole>? roles;

        if (!skipCache)
        {
            roles = userRolesCache.Get<List<UserRole>>(cacheKey);

            // Return cached roles if available
            if (roles != null)
            {
                return roles;
            }
        }

        // Load roles from database when not present in cache
        roles = new UsersRepository().DC.UsersRoles
            .Include(x => x.Role).ThenInclude(x => x.Permissions)
            .Include(x => x.User)
            .Where(x => x.UserId == userId)
            .ToList();

        // Store in cache if any roles were found
        if (roles.Any())
        {
            // Cache with configured expiration window
            userRolesCache.Set(cacheKey, roles, TimeSpan.FromHours(cacheExpiryTimeInHours));
        }

        return roles;
    }
}
