using AlGreenMES.Modules.Orders.Application.Commands.PauseWork;
using AlGreenMES.Modules.Orders.Application.Queries.Tablet.GetTabletActiveWork;
using AlGreenMES.Modules.Orders.Application.Queries.Tablet.GetTabletIncoming;
using AlGreenMES.Modules.Orders.Application.Queries.Tablet.GetTabletQueue;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Orders.Api.Controllers;

[ApiController]
[Route("api/tablet")]
[Authorize]
public class TabletController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantService _tenantService;

    public TabletController(IMediator mediator, ITenantService tenantService)
    {
        _mediator = mediator;
        _tenantService = tenantService;
    }

    [HttpGet("queue")]
    public async Task<IActionResult> GetQueue([FromQuery] Guid userId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTabletQueueQuery(_tenantService.GetCurrentTenantId(), userId), cancellationToken);
        return Ok(result);
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActiveWork([FromQuery] Guid userId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTabletActiveWorkQuery(_tenantService.GetCurrentTenantId(), userId), cancellationToken);
        return Ok(result);
    }

    [HttpGet("incoming")]
    public async Task<IActionResult> GetIncoming([FromQuery] Guid userId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTabletIncomingQuery(_tenantService.GetCurrentTenantId(), userId), cancellationToken);
        return Ok(result);
    }

    [HttpPost("pause")]
    public async Task<IActionResult> Pause([FromQuery] Guid userId, CancellationToken cancellationToken)
    {
        await _mediator.Send(new PauseWorkCommand(userId), cancellationToken);
        return Ok();
    }
}
