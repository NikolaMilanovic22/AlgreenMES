using AlGreenMES.Modules.Orders.Application.DTOs;
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
        List<string>? orderTypes,
        CancellationToken cancellationToken = default);

    Task<TimeTrackingReportDto> GetTimeTrackingReportAsync(
        Guid tenantId,
        DateTime? from,
        DateTime? to,
        Guid? processId,
        ComplexityType? complexity,
        string? orderNumber,
        List<Guid>? productCategoryIds,
        List<string>? orderTypes,
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
        List<string>? orderTypes,
        CancellationToken cancellationToken = default);

    Task<ProcessTimeTrendDto> GetProcessTimeTrendAsync(
        Guid tenantId,
        Guid processId,
        ComplexityType complexity,
        ReportGranularity granularity,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default);

    Task<ActiveProcessFunnelDto> GetActiveProcessFunnelAsync(
        Guid tenantId,
        List<string>? orderTypes,
        ComplexityType? complexity,
        CancellationToken cancellationToken = default);

    Task<BlocksPerProcessReportDto> GetBlocksPerProcessAsync(
        Guid tenantId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default);

    Task<ProductManufacturingTimeReportDto> GetProductManufacturingTimeAsync(
        Guid tenantId,
        DateTime? from,
        DateTime? to,
        List<string>? orderTypes,
        List<Guid>? productCategoryIds,
        CancellationToken cancellationToken = default);

    Task<WorkEfficiencyReportDto> GetWorkEfficiencyAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<ActiveWorkSessionDto?> GetActiveWorkSessionAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the worker has used up their MaxOvertimeHours for today —
    /// i.e. an OT re-login at this moment would have zero quota left and
    /// should be blocked at check-in. False if the worker still has any
    /// regular-shift or overtime time remaining today, or if no shift
    /// matches today (defensive: allow login when we can't compute a cap).
    /// Mirrors the cap math in <see cref="GetActiveWorkSessionAsync"/>.
    /// </summary>
    Task<bool> IsOvertimeQuotaExhaustedAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
