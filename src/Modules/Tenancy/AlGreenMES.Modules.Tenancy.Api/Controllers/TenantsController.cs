using AlGreenMES.Modules.Tenancy.Api.Requests;
using AlGreenMES.Modules.Tenancy.Application.Commands.CreateTenant;
using AlGreenMES.Modules.Tenancy.Application.Commands.UpdateTenant;
using AlGreenMES.Modules.Tenancy.Application.Commands.UpdateTenantSettings;
using AlGreenMES.Modules.Tenancy.Application.Queries.GetTenantById;
using AlGreenMES.Modules.Tenancy.Application.Queries.GetTenants;
using AlGreenMES.Modules.Tenancy.Application.Queries.GetTenantSettings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Tenancy.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "RequireSuperAdmin")]
public class TenantsController : ControllerBase
{
    private readonly IMediator _mediator;

    public TenantsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> GetTenants(
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
        var result = await _mediator.Send(new GetTenantsQuery
        {
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
    public async Task<IActionResult> GetTenantById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTenantByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateTenantCommand(
            request.Name, request.Code,
            request.DefaultWarningDays, request.DefaultCriticalDays,
            request.WarningColor, request.CriticalColor), cancellationToken);
        return CreatedAtAction(nameof(GetTenantById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateTenant(Guid id, [FromBody] UpdateTenantRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateTenantCommand(
            id, request.Name, request.IsActive,
            request.DefaultWarningDays, request.DefaultCriticalDays,
            request.WarningColor, request.CriticalColor), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}/settings")]
    public async Task<IActionResult> GetTenantSettings(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTenantSettingsQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPut("{id:guid}/settings")]
    public async Task<IActionResult> UpdateTenantSettings(Guid id, [FromBody] UpdateTenantSettingsRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateTenantSettingsCommand(
            id,
            request.DefaultWarningDays,
            request.DefaultCriticalDays,
            request.WarningColor,
            request.CriticalColor), cancellationToken);

        return Ok(result);
    }
}
