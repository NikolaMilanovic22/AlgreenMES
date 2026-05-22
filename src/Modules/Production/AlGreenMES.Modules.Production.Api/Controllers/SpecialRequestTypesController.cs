using AlGreenMES.Modules.Production.Api.Requests;
using AlGreenMES.Modules.Production.Application.Commands.ActivateSpecialRequestType;
using AlGreenMES.Modules.Production.Application.Commands.CreateSpecialRequestType;
using AlGreenMES.Modules.Production.Application.Commands.DeactivateSpecialRequestType;
using AlGreenMES.Modules.Production.Application.Commands.UpdateSpecialRequestType;
using AlGreenMES.Modules.Production.Application.Queries.GetSpecialRequestTypes;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Production.Api.Controllers;

[ApiController]
[Route("api/special-request-types")]
[Authorize]
public class SpecialRequestTypesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantService _tenantService;

    public SpecialRequestTypesController(IMediator mediator, ITenantService tenantService)
    {
        _mediator = mediator;
        _tenantService = tenantService;
    }

    [HttpGet]
    public async Task<IActionResult> GetSpecialRequestTypes(
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
        var result = await _mediator.Send(new GetSpecialRequestTypesQuery
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

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> CreateSpecialRequestType([FromBody] CreateSpecialRequestTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CreateSpecialRequestTypeCommand(
                _tenantService.GetCurrentTenantId(), request.Code, request.Name, request.Description,
                request.AddsProcesses, request.RemovesProcesses, request.OnlyProcesses),
            cancellationToken);
        return CreatedAtAction(nameof(GetSpecialRequestTypes), routeValues: null, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> UpdateSpecialRequestType(Guid id, [FromBody] UpdateSpecialRequestTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateSpecialRequestTypeCommand(
                id, request.Name, request.Description,
                request.AddsProcesses, request.RemovesProcesses, request.OnlyProcesses),
            cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> DeactivateSpecialRequestType(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeactivateSpecialRequestTypeCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> ActivateSpecialRequestType(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ActivateSpecialRequestTypeCommand(id), cancellationToken);
        return NoContent();
    }
}
