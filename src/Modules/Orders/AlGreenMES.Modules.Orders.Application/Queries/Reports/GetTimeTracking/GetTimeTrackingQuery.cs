using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Production.Domain.Enums;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.Reports.GetTimeTracking;

public record GetTimeTrackingQuery(
    Guid TenantId,
    DateTime? From,
    DateTime? To,
    Guid? ProcessId,
    ComplexityType? Complexity,
    string? OrderNumber,
    List<Guid>? ProductCategoryIds,
    List<OrderType>? OrderTypes) : IRequest<TimeTrackingReportDto>;
