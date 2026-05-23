using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Orders.Application.Queries.Reports.GetDeliveryCompliance;
using AlGreenMES.Modules.Orders.Application.Queries.Reports.GetProcessTimes;
using AlGreenMES.Modules.Orders.Application.Queries.Reports.GetTimeTracking;
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
