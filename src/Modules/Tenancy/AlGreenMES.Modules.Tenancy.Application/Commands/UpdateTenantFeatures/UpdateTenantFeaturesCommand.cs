using AlGreenMES.Modules.Tenancy.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Tenancy.Application.Commands.UpdateTenantFeatures;

public record UpdateTenantFeaturesCommand(Guid TenantId, List<string> DisabledFeatures) : IRequest<TenantDto>;
