using AlGreenMES.Modules.Tenancy.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Tenancy.Application.Commands.UnblockTenant;

public record UnblockTenantCommand(Guid TenantId) : IRequest<TenantDto>;
