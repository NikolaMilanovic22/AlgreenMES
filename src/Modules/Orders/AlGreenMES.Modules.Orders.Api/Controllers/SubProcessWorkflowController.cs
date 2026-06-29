using AlGreenMES.Modules.Orders.Api.Requests;
using AlGreenMES.Modules.Orders.Application.Commands.CompleteSubProcess;
using AlGreenMES.Modules.Orders.Application.Commands.StartSubProcess;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Orders.Api.Controllers;

[ApiController]
[Route("api/order-item-sub-processes")]
[Authorize]
public class SubProcessWorkflowController : ControllerBase
{
    private readonly IMediator _mediator;

    public SubProcessWorkflowController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("{id:guid}/start")]
    public async Task<IActionResult> StartSubProcess(Guid id, [FromBody] StartSubProcessRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new StartSubProcessCommand(id, request.UserId, request.OccurredAt, request.ActionId), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> CompleteSubProcess(Guid id, [FromBody] CompleteSubProcessRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CompleteSubProcessCommand(id, request.UserId, request.OccurredAt, request.ActionId), cancellationToken);
        return Ok(result);
    }
}
