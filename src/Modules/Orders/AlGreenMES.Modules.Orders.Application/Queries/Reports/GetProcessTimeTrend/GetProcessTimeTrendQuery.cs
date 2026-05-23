using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using AlGreenMES.Modules.Orders.Application.Queries.Reports.GetDeliveryCompliance;
using AlGreenMES.Modules.Production.Domain.Enums;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.Reports.GetProcessTimeTrend;

public record GetProcessTimeTrendQuery(
    Guid TenantId,
    Guid ProcessId,
    ComplexityType Complexity,
    ReportGranularity Granularity,
    DateTime? From,
    DateTime? To) : IRequest<ProcessTimeTrendDto>;
