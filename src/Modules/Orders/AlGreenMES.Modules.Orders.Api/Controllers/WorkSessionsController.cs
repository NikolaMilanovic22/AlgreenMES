using AlGreenMES.Modules.Orders.Api.Requests;
using AlGreenMES.Modules.Orders.Application.Commands.CheckIn;
using AlGreenMES.Modules.Orders.Application.Commands.CheckOut;
using AlGreenMES.Modules.Orders.Application.Queries.GetActiveWorkSession;
using AlGreenMES.Modules.Orders.Application.Queries.GetWorkSessions;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Orders.Api.Controllers;

[ApiController]
[Route("api/work-sessions")]
[Authorize]
public class WorkSessionsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantService _tenantService;

    private readonly ICurrentUserService _currentUserService;

    public WorkSessionsController(IMediator mediator, ITenantService tenantService, ICurrentUserService currentUserService)
    {
        _mediator = mediator;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// Calling worker's currently-open WorkSession + pre-computed alarm /
    /// auto-logout timestamps for the tablet countdown banner. Returns null
    /// (HTTP 204) when no session is open. Bojan spec 25.05.2026.
    /// </summary>
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetActiveWorkSessionQuery(_tenantService.GetCurrentTenantId(), _currentUserService.GetCurrentUserId()),
            cancellationToken);
        return result == null ? NoContent() : Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetWorkSessions(
        [FromQuery] DateOnly date,
        [FromQuery] Guid? userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetWorkSessionsQuery
        {
            TenantId = _tenantService.GetCurrentTenantId(),
            Date = date,
            UserId = userId,
            Page = page,
            PageSize = pageSize
        }, cancellationToken);
        return Ok(result);
    }

    [HttpPost("check-in")]
    public async Task<IActionResult> CheckIn([FromBody] CheckInRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CheckInCommand(_tenantService.GetCurrentTenantId(), request.UserId),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("check-out")]
    public async Task<IActionResult> CheckOut([FromBody] CheckOutRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CheckOutCommand(request.UserId),
            cancellationToken);
        return Ok(result);
    }
}
