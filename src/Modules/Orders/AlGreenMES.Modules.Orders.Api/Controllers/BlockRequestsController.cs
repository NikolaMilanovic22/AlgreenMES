using AlGreenMES.Modules.Orders.Api.Requests;
using AlGreenMES.Modules.Orders.Application.Commands.ApproveBlockRequest;
using AlGreenMES.Modules.Orders.Application.Commands.CreateBlockRequest;
using AlGreenMES.Modules.Orders.Application.Commands.RejectBlockRequest;
using AlGreenMES.Modules.Orders.Application.Queries.GetBlockRequests;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Orders.Api.Controllers;

[ApiController]
[Route("api/block-requests")]
[Authorize]
public class BlockRequestsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantService _tenantService;

    public BlockRequestsController(IMediator mediator, ITenantService tenantService)
    {
        _mediator = mediator;
        _tenantService = tenantService;
    }

    [HttpGet]
    public async Task<IActionResult> GetBlockRequests(
        [FromQuery] RequestStatus? status,
        [FromQuery] Guid? orderId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetBlockRequestsQuery
        {
            TenantId = _tenantService.GetCurrentTenantId(),
            Status = status,
            OrderId = orderId,
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
    public async Task<IActionResult> CreateBlockRequest([FromBody] CreateBlockRequestRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CreateBlockRequestCommand(_tenantService.GetCurrentTenantId(), request.OrderItemProcessId, request.OrderItemSubProcessId, request.RequestedByUserId, request.RequestNote),
            cancellationToken);
        return CreatedAtAction(nameof(GetBlockRequests), result);
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Coordinator")]
    public async Task<IActionResult> ApproveBlockRequest(Guid id, [FromBody] HandleBlockRequestRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ApproveBlockRequestCommand(id, request.HandledByUserId, request.Note ?? string.Empty),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Coordinator")]
    public async Task<IActionResult> RejectBlockRequest(Guid id, [FromBody] HandleBlockRequestRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RejectBlockRequestCommand(id, request.HandledByUserId, request.Note),
            cancellationToken);
        return Ok(result);
    }
}
