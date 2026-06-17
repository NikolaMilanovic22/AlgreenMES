using AlGreenMES.Modules.Tenancy.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Tenancy.Application.Commands.UpdateTenant;

public record UpdateTenantCommand(
    Guid Id,
    string Name,
    int? DefaultWarningDays = null,
    int? DefaultCriticalDays = null,
    string? WarningColor = null,
    string? CriticalColor = null) : IRequest<TenantDto>;
