using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using AlGreenMES.Modules.Orders.Domain.Enums;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.Reports.GetProcessTimes;

public record GetProcessTimesQuery(
    Guid TenantId,
    DateTime? From,
    DateTime? To,
    List<Guid>? ProductCategoryIds,
    List<OrderType>? OrderTypes) : IRequest<ProcessTimesDto>;
