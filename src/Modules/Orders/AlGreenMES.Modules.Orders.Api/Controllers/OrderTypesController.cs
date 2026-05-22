using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Orders.Api.Requests;
using AlGreenMES.Modules.Orders.Application.Commands.CreateOrderType;
using AlGreenMES.Modules.Orders.Application.Commands.DeleteOrderType;
using AlGreenMES.Modules.Orders.Application.Commands.UpdateOrderType;
using AlGreenMES.Modules.Orders.Application.Queries.GetOrderTypes;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Orders.Api.Controllers;

[ApiController]
[Route("api/order-types")]
[Authorize]
public class OrderTypesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantService _tenantService;

    public OrderTypesController(IMediator mediator, ITenantService tenantService)
    {
        _mediator = mediator;
        _tenantService = tenantService;
    }

    [HttpGet]
    public async Task<IActionResult> GetOrderTypes(
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
        var result = await _mediator.Send(new GetOrderTypesQuery
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
    public async Task<IActionResult> CreateOrderType([FromBody] CreateOrderTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CreateOrderTypeCommand(
                _tenantService.GetCurrentTenantId(),
                request.Code,
                request.Name,
                request.AllowsManualProcesses),
            cancellationToken);
        return CreatedAtAction(nameof(GetOrderTypes), routeValues: null, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> UpdateOrderType(Guid id, [FromBody] UpdateOrderTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateOrderTypeCommand(
                id,
                request.Name,
                request.AllowsManualProcesses,
                request.IsActive),
            cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> DeleteOrderType(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteOrderTypeCommand(id), cancellationToken);
        return Ok(result);
    }
}
