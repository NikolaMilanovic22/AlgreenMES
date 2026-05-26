using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Orders.Application.Queries.Reports.GetActiveProcessFunnel;
using AlGreenMES.Modules.Orders.Application.Queries.Reports.GetBlocksPerProcess;
using AlGreenMES.Modules.Orders.Application.Queries.Reports.GetDeliveryCompliance;
using AlGreenMES.Modules.Orders.Application.Queries.Reports.GetProcessTimes;
using AlGreenMES.Modules.Orders.Application.Queries.Reports.GetProcessTimeTrend;
using AlGreenMES.Modules.Orders.Application.Queries.Reports.GetProductManufacturingTime;
using AlGreenMES.Modules.Orders.Application.Queries.Reports.GetTimeTracking;
using AlGreenMES.Modules.Orders.Application.Queries.Reports.GetWorkEfficiency;
using AlGreenMES.Modules.Orders.Application.Queries.Reports.GetWorkerHours;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Production.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Orders.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantService _tenantService;

    public ReportsController(IMediator mediator, ITenantService tenantService)
    {
        _mediator = mediator;
        _tenantService = tenantService;
    }

    [HttpGet("process-times")]
    public async Task<IActionResult> GetProcessTimes(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] List<Guid>? productCategoryIds,
        [FromQuery] List<OrderType>? orderTypes,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetProcessTimesQuery(_tenantService.GetCurrentTenantId(), from, to, productCategoryIds, orderTypes),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("time-tracking")]
    public async Task<IActionResult> GetTimeTracking(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] Guid? processId,
        [FromQuery] ComplexityType? complexity,
        [FromQuery] string? orderNumber,
        [FromQuery] List<Guid>? productCategoryIds,
        [FromQuery] List<OrderType>? orderTypes,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetTimeTrackingQuery(_tenantService.GetCurrentTenantId(), from, to, processId, complexity, orderNumber, productCategoryIds, orderTypes),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("worker-hours")]
    public async Task<IActionResult> GetWorkerHours(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? userId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetWorkerHoursQuery(_tenantService.GetCurrentTenantId(), from, to, userId),
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// "Efikasnost radnog vremena" — per-worker per-day breakdown of
    /// worked time vs. active-on-process time. Aktivno = wall-clock union
    /// of subprocess log ranges (parallel work counted once). Efficiency %
    /// gets color-coded on the FE. Bojan spec 25.05.2026.
    /// </summary>
    [HttpGet("work-efficiency")]
    public async Task<IActionResult> GetWorkEfficiency(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? userId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetWorkEfficiencyQuery(_tenantService.GetCurrentTenantId(), from, to, userId),
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// "Trend prosečnog vremena po nedelji" — per-period (week/month) stats
    /// for a single (process × complexity): green band = window-clamped
    /// MIN/MAX per bucket, blue line = Realni prosek per bucket, Normativ =
    /// 85% of trimmed mean across the whole filtered period (constant).
    /// </summary>
    [HttpGet("process-time-trend")]
    public async Task<IActionResult> GetProcessTimeTrend(
        [FromQuery] Guid processId,
        [FromQuery] ComplexityType complexity,
        [FromQuery] ReportGranularity granularity,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetProcessTimeTrendQuery(
                _tenantService.GetCurrentTenantId(),
                processId,
                complexity,
                granularity,
                from,
                to),
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// "Blokade po procesu" — per-process count of block requests + average
    /// duration in WORKING HOURS (intersection of CreatedAt → HandledAt with
    /// the union of active Shift windows). Approved = Approved+Resolved.
    /// Rejected blocks contribute zero duration to the average but count
    /// toward TotalSubmitted. Bojan spec 25.05.2026.
    /// </summary>
    [HttpGet("blocks-per-process")]
    public async Task<IActionResult> GetBlocksPerProcess(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetBlocksPerProcessQuery(_tenantService.GetCurrentTenantId(), from, to),
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// "Napredak aktivnih narudžbina" — per-process count of active
    /// OrderItemProcesses split into three buckets: U toku (InProgress),
    /// Spreman za izvršavanje (Pending + deps complete), Blokirano.
    /// </summary>
    [HttpGet("active-process-funnel")]
    public async Task<IActionResult> GetActiveProcessFunnel(
        [FromQuery] List<OrderType>? orderTypes,
        [FromQuery] ComplexityType? complexity,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetActiveProcessFunnelQuery(_tenantService.GetCurrentTenantId(), orderTypes, complexity),
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// "Prosečno trajanje izrade proizvoda" — per-completed-order breakdown
    /// of process timings + inter-process gaps. Najzastupljenija težina with
    /// low-bias tie-break (T/S=S, S/L=L, T/L=L). Overlapping processes are
    /// clipped (no negative gaps). Bojan spec 25.05.2026.
    /// </summary>
    [HttpGet("product-manufacturing-time")]
    public async Task<IActionResult> GetProductManufacturingTime(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] List<OrderType>? orderTypes,
        [FromQuery] List<Guid>? productCategoryIds,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetProductManufacturingTimeQuery(_tenantService.GetCurrentTenantId(), from, to, orderTypes, productCategoryIds),
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// "Analiza kašnjenja i poštovanja rokova" — per-period on-time vs late
    /// breakdown of completed orders. CompletedAt &lt;= DeliveryDate → on time;
    /// otherwise late. Bucketed by ISO week (Monday) or month.
    /// </summary>
    [HttpGet("delivery-compliance")]
    public async Task<IActionResult> GetDeliveryCompliance(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] ReportGranularity granularity,
        [FromQuery] List<OrderType>? orderTypes,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetDeliveryComplianceQuery(_tenantService.GetCurrentTenantId(), from, to, granularity, orderTypes),
            cancellationToken);
        return Ok(result);
    }
}
