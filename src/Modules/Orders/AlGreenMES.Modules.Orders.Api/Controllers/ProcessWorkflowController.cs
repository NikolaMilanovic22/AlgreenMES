using AlGreenMES.Modules.Orders.Api.Requests;
using AlGreenMES.Modules.Orders.Application.Commands.BlockProcess;
using AlGreenMES.Modules.Orders.Application.Commands.CompleteProcess;
using AlGreenMES.Modules.Orders.Application.Commands.PauseStation;
using AlGreenMES.Modules.Orders.Application.Commands.ResumeProcessWork;
using AlGreenMES.Modules.Orders.Application.Commands.ResumeStation;
using AlGreenMES.Modules.Orders.Application.Commands.StartProcessWork;
using AlGreenMES.Modules.Orders.Application.Commands.StopProcessWork;
using AlGreenMES.Modules.Orders.Application.Commands.RestartProcess;
using AlGreenMES.Modules.Orders.Application.Commands.SetProcessExcludedFromReports;
using AlGreenMES.Modules.Orders.Application.Commands.UnblockProcess;
using AlGreenMES.Modules.Orders.Application.Commands.WithdrawProcess;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Orders.Api.Controllers;

[ApiController]
[Route("api/order-item-processes")]
[Authorize]
public class ProcessWorkflowController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantService _tenantService;

    public ProcessWorkflowController(IMediator mediator, ITenantService tenantService)
    {
        _mediator = mediator;
        _tenantService = tenantService;
    }

    [HttpPost("{id:guid}/block")]
    public async Task<IActionResult> BlockProcess(Guid id, [FromBody] BlockProcessRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new BlockProcessCommand(id, request.UserId, request.Reason), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/unblock")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Coordinator")]
    public async Task<IActionResult> UnblockProcess(Guid id, [FromBody] UnblockProcessRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new UnblockProcessCommand(id, request.UserId, request.ResetTime), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> CompleteProcess(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new CompleteProcessCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/restart")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Coordinator")]
    public async Task<IActionResult> RestartProcess(Guid id, [FromBody] RestartProcessRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new RestartProcessCommand(id, request.ResetTime), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/withdraw")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Coordinator")]
    public async Task<IActionResult> WithdrawProcess(Guid id, [FromBody] WithdrawProcessRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new WithdrawProcessCommand(id, request.UserId, request.Reason), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/start")]
    public async Task<IActionResult> StartWork(Guid id, [FromBody] StartProcessWorkRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new StartProcessWorkCommand(id, request.UserId), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/stop")]
    public async Task<IActionResult> StopWork(Guid id, [FromBody] StopProcessWorkRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new StopProcessWorkCommand(id, request.UserId), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/resume")]
    public async Task<IActionResult> ResumeWork(Guid id, [FromBody] ResumeProcessWorkRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ResumeProcessWorkCommand(id, request.UserId), cancellationToken);
        return NoContent();
    }

    [HttpPost("pause-station")]
    public async Task<IActionResult> PauseStation([FromBody] StationRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new PauseStationCommand(request.ProcessId, _tenantService.GetCurrentTenantId(), request.UserId), cancellationToken);
        return NoContent();
    }

    [HttpPost("resume-station")]
    public async Task<IActionResult> ResumeStation([FromBody] StationRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ResumeStationCommand(request.ProcessId, _tenantService.GetCurrentTenantId(), request.UserId), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Toggle whether this OrderItemProcess is excluded from the /reports
    /// statistics + export (Sale/Bojan's manual Uključi/Isključi switch).
    /// </summary>
    [HttpPatch("{id:guid}/excluded-from-reports")]
    public async Task<IActionResult> SetExcludedFromReports(
        Guid id,
        [FromBody] SetProcessExcludedFromReportsRequest request,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new SetProcessExcludedFromReportsCommand(id, request.Excluded),
            cancellationToken);
        return NoContent();
    }
}
