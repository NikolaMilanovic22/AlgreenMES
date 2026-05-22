using AlGreenMES.Modules.Orders.Application.Queries.Dashboard.GetDashboardStatistics;
using AlGreenMES.Modules.Orders.Application.Queries.Dashboard.GetDeadlineWarnings;
using AlGreenMES.Modules.Orders.Application.Queries.Dashboard.GetLiveView;
using AlGreenMES.Modules.Orders.Application.Queries.Dashboard.GetPendingBlockRequests;
using AlGreenMES.Modules.Orders.Application.Queries.Dashboard.GetWorkersStatus;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Orders.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantService _tenantService;

    public DashboardController(IMediator mediator, ITenantService tenantService)
    {
        _mediator = mediator;
        _tenantService = tenantService;
    }

    [HttpGet("warnings")]
    public async Task<IActionResult> GetDeadlineWarnings(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetDeadlineWarningsQuery(_tenantService.GetCurrentTenantId()), cancellationToken);
        return Ok(result);
    }

    [HttpGet("live-view")]
    public async Task<IActionResult> GetLiveView(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetLiveViewQuery(_tenantService.GetCurrentTenantId()), cancellationToken);
        return Ok(result);
    }

    [HttpGet("workers-status")]
    public async Task<IActionResult> GetWorkersStatus(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetWorkersStatusQuery(_tenantService.GetCurrentTenantId()), cancellationToken);
        return Ok(result);
    }

    [HttpGet("pending-blocks")]
    public async Task<IActionResult> GetPendingBlockRequests(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPendingBlockRequestsQuery(_tenantService.GetCurrentTenantId()), cancellationToken);
        return Ok(result);
    }

    [HttpGet("statistics")]
    public async Task<IActionResult> GetDashboardStatistics(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetDashboardStatisticsQuery(_tenantService.GetCurrentTenantId()), cancellationToken);
        return Ok(result);
    }
}
