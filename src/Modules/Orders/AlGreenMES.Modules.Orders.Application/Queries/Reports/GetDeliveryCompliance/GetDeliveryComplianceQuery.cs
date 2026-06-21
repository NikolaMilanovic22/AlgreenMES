using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using AlGreenMES.Modules.Orders.Domain.Enums;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.Reports.GetDeliveryCompliance;

public enum ReportGranularity
{
    Week,
    Month
}

public record GetDeliveryComplianceQuery(
    Guid TenantId,
    DateTime? From,
    DateTime? To,
    ReportGranularity Granularity,
    List<string>? OrderTypes) : IRequest<DeliveryComplianceReportDto>;
