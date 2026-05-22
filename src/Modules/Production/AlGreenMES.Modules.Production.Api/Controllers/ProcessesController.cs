using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Production.Api.Requests;
using AlGreenMES.Modules.Production.Application.Commands.ActivateProcess;
using AlGreenMES.Modules.Production.Application.Commands.AddSubProcess;
using AlGreenMES.Modules.Production.Application.Commands.CreateProcess;
using AlGreenMES.Modules.Production.Application.Commands.DeactivateProcess;
using AlGreenMES.Modules.Production.Application.Commands.DeleteProcess;
using AlGreenMES.Modules.Production.Application.Commands.DeactivateSubProcess;
using AlGreenMES.Modules.Production.Application.Commands.ReorderProcesses;
using AlGreenMES.Modules.Production.Application.Commands.ReorderSubProcesses;
using AlGreenMES.Modules.Production.Application.Commands.UpdateProcess;
using AlGreenMES.Modules.Production.Application.Commands.UpdateSubProcess;
using AlGreenMES.Modules.Production.Application.Queries.GetProcessById;
using AlGreenMES.Modules.Production.Application.Queries.GetProcesses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Production.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProcessesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantService _tenantService;
    private readonly IProcessChangeNotifier _processChangeNotifier;

    public ProcessesController(IMediator mediator, ITenantService tenantService, IProcessChangeNotifier processChangeNotifier)
    {
        _mediator = mediator;
        _tenantService = tenantService;
        _processChangeNotifier = processChangeNotifier;
    }

    [HttpGet]
    public async Task<IActionResult> GetProcesses(
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetProcessesQuery
        {
            TenantId = _tenantService.GetCurrentTenantId(),
            IsActive = isActive,
            Page = page,
            PageSize = pageSize,
            Search = search,
            CreatedFrom = createdFrom,
            CreatedTo = createdTo,
            SortBy = sortBy,
            SortDirection = sortDirection
        }, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetProcessById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetProcessByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> CreateProcess([FromBody] CreateProcessRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CreateProcessCommand(
                _tenantService.GetCurrentTenantId(), request.Code, request.Name, request.SequenceOrder, null,
                request.SubProcesses?.Select(s => new CreateProcessSubProcessItem(s.Name, s.SequenceOrder)).ToList()),
            cancellationToken);
        await NotifyChangeAsync(cancellationToken);
        return CreatedAtAction(nameof(GetProcessById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> UpdateProcess(Guid id, [FromBody] UpdateProcessRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateProcessCommand(
                id, request.Code, request.Name, request.SequenceOrder,
                request.AddSubProcesses?.Select(s => new UpdateProcessSubProcessAdd(s.Name, s.SequenceOrder)).ToList(),
                request.DeactivateSubProcessIds),
            cancellationToken);
        await NotifyChangeAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost("reorder")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> ReorderProcesses([FromBody] ReorderProcessesRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ReorderProcessesCommand(
            request.Items.Select(i => new Application.Commands.ReorderProcesses.ReorderProcessesItem(i.Id, i.SequenceOrder)).ToList()),
            cancellationToken);
        await NotifyChangeAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> DeleteProcess(Guid id, [FromQuery] bool forceDeactivate = false, [FromQuery] bool forceDelete = false, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new DeleteProcessCommand(id, forceDeactivate, forceDelete), cancellationToken);
        await NotifyChangeAsync(cancellationToken);
        if (!result.HardDeleted && !result.Deactivated)
            return Ok(new { hasReferences = true, referencedOrderCount = result.ReferencedOrderCount });
        return NoContent();
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> ActivateProcess(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ActivateProcessCommand(id), cancellationToken);
        await NotifyChangeAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{processId:guid}/sub-processes")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> AddSubProcess(Guid processId, [FromBody] AddSubProcessRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new AddSubProcessCommand(processId, request.Name, request.SequenceOrder),
            cancellationToken);
        await NotifyChangeAsync(cancellationToken);
        return CreatedAtAction(nameof(GetProcessById), new { id = processId }, result);
    }

    [HttpPut("{processId:guid}/sub-processes/{subProcessId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> UpdateSubProcess(Guid processId, Guid subProcessId, [FromBody] UpdateSubProcessRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateSubProcessCommand(processId, subProcessId, request.Name, request.SequenceOrder),
            cancellationToken);
        await NotifyChangeAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost("{processId:guid}/sub-processes/reorder")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> ReorderSubProcesses(Guid processId, [FromBody] ReorderSubProcessesRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ReorderSubProcessesCommand(
            processId,
            request.Items.Select(i => new Application.Commands.ReorderSubProcesses.ReorderSubProcessesItem(i.Id, i.SequenceOrder)).ToList()),
            cancellationToken);
        await NotifyChangeAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{processId:guid}/sub-processes/{subProcessId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> DeactivateSubProcess(Guid processId, Guid subProcessId, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeactivateSubProcessCommand(processId, subProcessId), cancellationToken);
        await NotifyChangeAsync(cancellationToken);
        return NoContent();
    }

    private Task NotifyChangeAsync(CancellationToken cancellationToken)
    {
        // ITenantService now throws on missing JWT, so the caller is always authenticated here.
        return _processChangeNotifier.NotifyProcessDefinitionChangedAsync(
            _tenantService.GetCurrentTenantId(), cancellationToken);
    }
}
