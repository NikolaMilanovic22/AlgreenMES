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

    public ReportsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("process-times")]
    public async Task<IActionResult> GetProcessTimes(
        [FromQuery] Guid tenantId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] List<Guid>? productCategoryIds,
        [FromQuery] List<OrderType>? orderTypes,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetProcessTimesQuery(tenantId, from, to, productCategoryIds, orderTypes),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("time-tracking")]
    public async Task<IActionResult> GetTimeTracking(
        [FromQuery] Guid tenantId,
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
            new GetTimeTrackingQuery(tenantId, from, to, processId, complexity, orderNumber, productCategoryIds, orderTypes),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("worker-hours")]
    public async Task<IActionResult> GetWorkerHours(
        [FromQuery] Guid tenantId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? userId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetWorkerHoursQuery(tenantId, from, to, userId),
            cancellationToken);
        return Ok(result);
    }
}
