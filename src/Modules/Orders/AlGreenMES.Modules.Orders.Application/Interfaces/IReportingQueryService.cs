using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using AlGreenMES.Modules.Orders.Application.Queries.Reports.GetDeliveryCompliance;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Production.Domain.Enums;

namespace AlGreenMES.Modules.Orders.Application.Interfaces;

public interface IReportingQueryService
{
    Task<ProcessTimesDto> GetProcessTimesAsync(
        Guid tenantId,
        DateTime? from,
        DateTime? to,
        List<Guid>? productCategoryIds,
        List<OrderType>? orderTypes,
        CancellationToken cancellationToken = default);

    Task<TimeTrackingReportDto> GetTimeTrackingReportAsync(
        Guid tenantId,
        DateTime? from,
        DateTime? to,
        Guid? processId,
        ComplexityType? complexity,
        string? orderNumber,
        List<Guid>? productCategoryIds,
        List<OrderType>? orderTypes,
        CancellationToken cancellationToken = default);

    Task<WorkerHoursReportDto> GetWorkerHoursReportAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<DeliveryComplianceReportDto> GetDeliveryComplianceAsync(
        Guid tenantId,
        DateTime? from,
        DateTime? to,
        ReportGranularity granularity,
        List<OrderType>? orderTypes,
        CancellationToken cancellationToken = default);
}
