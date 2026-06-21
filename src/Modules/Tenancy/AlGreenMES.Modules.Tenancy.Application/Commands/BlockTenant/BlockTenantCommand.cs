using AlGreenMES.Modules.Tenancy.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Tenancy.Application.Commands.BlockTenant;

public record BlockTenantCommand(Guid TenantId, string? Reason) : IRequest<TenantDto>;
