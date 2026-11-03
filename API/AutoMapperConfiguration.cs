using AutoMapper;
using PlaymoxAPI.BLL.BO.Platform;
using PlaymoxAPI.BLL.BO.Users;
using PlaymoxAPI.BLL.DTO.Platform;
using PlaymoxAPI.BLL.DTO.Users;

namespace PlaymoxAPI;

public class AutoMapperConfiguration
{
    public static MapperConfiguration DefaultConfiguration()
    {
        return new MapperConfiguration(cfg => {
            cfg.CreateMap<User, UserDto>().ReverseMap();
            cfg.CreateMap<UserRole, UserRoleDto>().ReverseMap();
            cfg.CreateMap<Role, RoleDto>().ReverseMap();
            cfg.CreateMap<RolePermission, RolePermissionDto>().ReverseMap();
            cfg.CreateMap<UserQuota, UserQuotaDto>().ReverseMap();
            cfg.CreateMap<VirtualGameInstance, VirtualGameInstanceDto>().ReverseMap();
            cfg.CreateMap<GameDefinition, GameDefinitionDto>().ReverseMap();
            cfg.CreateMap<ProvisioningJob, ProvisioningJobDto>().ReverseMap();
            cfg.CreateMap<ProxmoxSettings, ProxmoxSettingsDto>().ReverseMap();
        });
    }
}
