using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Production.Domain.Enums;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.Reports.GetActiveProcessFunnel;

public record GetActiveProcessFunnelQuery(
    Guid TenantId,
    List<OrderType>? OrderTypes,
    ComplexityType? Complexity) : IRequest<ActiveProcessFunnelDto>;
