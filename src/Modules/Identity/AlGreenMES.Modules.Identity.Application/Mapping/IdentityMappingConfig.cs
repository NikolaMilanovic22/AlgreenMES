using AlGreenMES.Modules.Identity.Application.DTOs;
using AlGreenMES.Modules.Identity.Domain.Entities;
using Mapster;

namespace AlGreenMES.Modules.Identity.Application.Mapping;

public static class IdentityMappingConfig
{
    public static void Register(TypeAdapterConfig config)
    {
        config.NewConfig<User, UserDto>()
            .Map(dest => dest.Processes, src => src.UserProcesses.Select(up => new UserProcessDto(up.ProcessId)).ToList())
            .Map(dest => dest.AdditionalRoles, src => src.AdditionalRoles.Select(r => r.Role).ToList());

        config.NewConfig<Shift, ShiftDto>();
    }
}
