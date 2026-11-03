using PlaymoxAPI.BLL;
using PlaymoxAPI.BLL.BO.Platform;
using PlaymoxAPI.BLL.BO.Users;
using PlaymoxAPI.Extensions.Auth;
using Microsoft.EntityFrameworkCore;

namespace PlaymoxAPI.Helpers;

public static class BootstrapDataSeeder
{
    public static void SeedInitialData(PlaymoxContext dbContext, IConfiguration configuration, ILogger logger)
    {
        var adminLogin = configuration["Bootstrap:Admin:Login"] ?? "admin";
        var adminPassword = configuration["Bootstrap:Admin:Password"];
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            throw new InvalidOperationException("Bootstrap:Admin:Password is not configured.");
        }

        var adminFirstName = configuration["Bootstrap:Admin:FirstName"] ?? "Playmox";
        var adminLastName = configuration["Bootstrap:Admin:LastName"] ?? "Admin";

        var userRole = EnsureRole(dbContext, "User");
        var adminRole = EnsureRole(dbContext, "Admin");

        var adminUser = dbContext.Users
            .Include(x => x.UserRoles)
            .FirstOrDefault(x => x.Login == adminLogin);

        if (adminUser == null)
        {
            adminUser = new User
            {
                Login = adminLogin,
                FirstName = adminFirstName,
                LastName = adminLastName,
                PasswordHash = Crypto.HashPassword(adminPassword)
            };
            dbContext.Users.Add(adminUser);
            dbContext.SaveChanges();
            logger.LogInformation("Bootstrap admin account created: {Login}", adminLogin);
        }
        else
        {
            adminUser.Login = adminLogin;
            adminUser.FirstName = adminFirstName;
            adminUser.LastName = adminLastName;
            adminUser.PasswordHash = Crypto.HashPassword(adminPassword);
            dbContext.SaveChanges();
            logger.LogInformation("Bootstrap admin account updated: {Login}", adminLogin);
        }

        var hasAdminRole = dbContext.UsersRoles.Any(x => x.UserId == adminUser.Id && x.RoleId == adminRole.Id);
        if (!hasAdminRole)
        {
            dbContext.UsersRoles.Add(new UserRole
            {
                UserId = adminUser.Id,
                RoleId = adminRole.Id
            });
            logger.LogInformation("Admin role assigned to bootstrap account.");
        }

        var hasUserRole = dbContext.UsersRoles.Any(x => x.UserId == adminUser.Id && x.RoleId == userRole.Id);
        if (!hasUserRole)
        {
            dbContext.UsersRoles.Add(new UserRole
            {
                UserId = adminUser.Id,
                RoleId = userRole.Id
            });
            logger.LogInformation("User role assigned to bootstrap admin account.");
        }

        var quota = dbContext.UsersQuotas.FirstOrDefault(x => x.UserId == adminUser.Id);
        if (quota == null)
        {
            quota = new UserQuota
            {
                UserId = adminUser.Id
            };
            dbContext.UsersQuotas.Add(quota);
        }

        quota.CpuCoresLimit = 32;
        quota.MemoryMbLimit = 65536;
        quota.MaxInstancesLimit = 20;
        quota.DiskGbLimit = 1000;
        quota.MaxPublicPortsLimit = 200;
        quota.AllowSshPublicPort = true;

        EnsureGameDefinitions(dbContext, logger);
        dbContext.SaveChanges();
        logger.LogInformation("Bootstrap admin quota ensured.");
    }

    private static Role EnsureRole(PlaymoxContext dbContext, string roleName)
    {
        var role = dbContext.Roles.FirstOrDefault(x => x.Name == roleName);
        if (role != null)
        {
            return role;
        }

        role = new Role
        {
            Name = roleName
        };
        dbContext.Roles.Add(role);
        dbContext.SaveChanges();
        return role;
    }

    private static void EnsureGameDefinitions(PlaymoxContext dbContext, ILogger logger)
    {
        UpsertGameDefinition(dbContext, new GameDefinition
        {
            Key = "blank-vm",
            DisplayName = "Blank VM (No Game)",
            LinuxGsmServerId = string.Empty,
            IsActive = true,
            DefaultPort = null,
            RequiredPublicPorts = 0,
            JsonSchema = """
                         {
                           "type": "object",
                           "title": "No game parameters",
                           "properties": {}
                         }
                         """
        });

        UpsertGameDefinition(dbContext, new GameDefinition
        {
            Key = "minecraft-java",
            DisplayName = "Minecraft Java",
            LinuxGsmServerId = "mcserver",
            IsActive = true,
            DefaultPort = 25565,
            RequiredPublicPorts = 1,
            JsonSchema = """
{
  "type": "object",
  "properties": {
    "serverVersion": { "type": "string", "title": "Server version (optional)" },
    "onlineMode": { "type": "boolean", "title": "Mojang online mode (online-mode)" },
    "maxPlayers": { "type": "integer", "minimum": 1, "maximum": 200, "title": "Max players" },
    "motd": { "type": "string", "title": "Server name / MOTD" },
    "difficulty": { "type": "string", "enum": ["peaceful", "easy", "normal", "hard"], "title": "Difficulty" },
    "gameMode": { "type": "string", "enum": ["survival", "creative", "adventure", "spectator"], "title": "Default game mode" },
    "pvp": { "type": "boolean", "title": "PvP" },
    "allowFlight": { "type": "boolean", "title": "Allow flight" },
    "viewDistance": { "type": "integer", "minimum": 3, "maximum": 32, "title": "View distance" },
    "simulationDistance": { "type": "integer", "minimum": 3, "maximum": 32, "title": "Simulation distance" },
    "spawnProtection": { "type": "integer", "minimum": 0, "maximum": 128, "title": "Spawn protection (blocks)" },
    "enableCommandBlock": { "type": "boolean", "title": "Enable command blocks" },
    "whiteList": { "type": "boolean", "title": "Whitelist" },
    "enforceWhitelist": { "type": "boolean", "title": "Enforce whitelist" },
    "allowNether": { "type": "boolean", "title": "Allow Nether" },
    "hardcore": { "type": "boolean", "title": "Hardcore mode" }
  }
}
"""
        });

        UpsertGameDefinition(dbContext, new GameDefinition
        {
            Key = "terraria",
            DisplayName = "Terraria",
            LinuxGsmServerId = "terrariaserver",
            IsActive = true,
            DefaultPort = 7777,
            RequiredPublicPorts = 1,
            JsonSchema = """
{
  "type": "object",
  "properties": {
    "worldName": { "type": "string", "title": "World name" },
    "worldSeed": { "type": "string", "title": "World seed (optional)" },
    "autoCreateSize": { "type": "integer", "enum": [1, 2, 3], "title": "World size (1=small, 2=medium, 3=large)" },
    "maxPlayers": { "type": "integer", "minimum": 1, "maximum": 255, "title": "Max players" },
    "password": { "type": "string", "title": "Server password (optional)" },
    "motd": { "type": "string", "title": "MOTD" }
  }
}
"""
        });

        UpsertGameDefinition(dbContext, new GameDefinition
        {
            Key = "unturned",
            DisplayName = "Unturned",
            LinuxGsmServerId = "untserver",
            IsActive = true,
            DefaultPort = 27015,
            RequiredPublicPorts = 2,
            JsonSchema = """
{
  "type": "object",
  "properties": {
    "serverName": { "type": "string", "title": "Server name" },
    "map": { "type": "string", "enum": ["PEI", "Washington", "Yukon", "Russia", "Germany", "Hawaii"], "title": "Map", "default": "PEI" },
    "mode": { "type": "string", "enum": ["easy", "normal", "hard"], "title": "Difficulty mode" },
    "maxPlayers": { "type": "integer", "minimum": 1, "maximum": 64, "title": "Max players" },
    "pvp": { "type": "boolean", "title": "PvP" },
    "password": { "type": "string", "title": "Server password (optional)" },
    "perspective": { "type": "string", "enum": ["both", "first", "third", "vehicle"], "title": "Perspective" },
    "cheats": { "type": "boolean", "title": "Allow cheats" },
    "welcomeMessage": { "type": "string", "title": "Welcome message" }
  }
}
"""
        });

        UpsertGameDefinition(dbContext, new GameDefinition
        {
            Key = "factorio",
            DisplayName = "Factorio",
            LinuxGsmServerId = "fctrserver",
            IsActive = true,
            DefaultPort = 34197,
            RequiredPublicPorts = 1,
            JsonSchema = """
{
  "type": "object",
  "properties": {
    "serverName": { "type": "string", "title": "Server name" },
    "description": { "type": "string", "title": "Server description" },
    "maxPlayers": { "type": "integer", "minimum": 1, "maximum": 255, "title": "Max players" },
    "visibilityPublic": { "type": "boolean", "title": "Public in server browser" },
    "password": { "type": "string", "title": "Server password (optional)" }
  }
}
"""
        });

        UpsertGameDefinition(dbContext, new GameDefinition
        {
            Key = "cs2",
            DisplayName = "Counter-Strike 2",
            LinuxGsmServerId = "cs2server",
            IsActive = false,
            DefaultPort = 27015,
            RequiredPublicPorts = 1,
            JsonSchema = """
                         {
                           "type": "object",
                           "properties": {
                             "hostname": { "type": "string", "title": "Server name" },
                             "maxPlayers": { "type": "integer", "minimum": 2, "maximum": 64, "title": "Max players" }
                           }
                         }
                         """
        });

        logger.LogInformation("Ensured default game definitions.");
    }

    private static void UpsertGameDefinition(PlaymoxContext dbContext, GameDefinition source)
    {
        var existing = dbContext.GameDefinitions.FirstOrDefault(x => x.Key == source.Key);
        if (existing == null)
        {
            dbContext.GameDefinitions.Add(source);
            return;
        }

        existing.DisplayName = source.DisplayName;
        existing.LinuxGsmServerId = source.LinuxGsmServerId;
        existing.IsActive = source.IsActive;
        existing.DefaultPort = source.DefaultPort;
        existing.RequiredPublicPorts = source.RequiredPublicPorts;
        existing.JsonSchema = source.JsonSchema;
    }
}
