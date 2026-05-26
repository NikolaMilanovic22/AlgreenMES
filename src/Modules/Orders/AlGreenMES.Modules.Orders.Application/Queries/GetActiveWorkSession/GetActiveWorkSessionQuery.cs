using AlGreenMES.Modules.Orders.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.GetActiveWorkSession;

public record GetActiveWorkSessionQuery(Guid TenantId, Guid UserId)
    : IRequest<ActiveWorkSessionDto?>;
