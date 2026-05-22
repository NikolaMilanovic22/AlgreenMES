using AlGreenMES.Modules.Orders.Api.Requests;
using AlGreenMES.Modules.Orders.Application.Commands.ApproveChangeRequest;
using AlGreenMES.Modules.Orders.Application.Commands.CreateChangeRequest;
using AlGreenMES.Modules.Orders.Application.Commands.RejectChangeRequest;
using AlGreenMES.Modules.Orders.Application.Queries.GetChangeRequests;
using AlGreenMES.Modules.Orders.Application.Queries.GetMyChangeRequests;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Orders.Api.Controllers;

[ApiController]
[Route("api/change-requests")]
[Authorize]
public class ChangeRequestsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantService _tenantService;

    public ChangeRequestsController(IMediator mediator, ITenantService tenantService)
    {
        _mediator = mediator;
        _tenantService = tenantService;
    }

    [HttpGet]
    public async Task<IActionResult> GetChangeRequests(
        [FromQuery] RequestStatus? status,
        [FromQuery] ChangeRequestType? requestType,
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
        var result = await _mediator.Send(new GetChangeRequestsQuery
        {
            TenantId = _tenantService.GetCurrentTenantId(),
            Status = status,
            RequestType = requestType,
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

    [HttpGet("my")]
    public async Task<IActionResult> GetMyChangeRequests(
        [FromQuery] Guid userId,
        [FromQuery] RequestStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetMyChangeRequestsQuery
        {
            TenantId = _tenantService.GetCurrentTenantId(),
            UserId = userId,
            Status = status,
            Page = page,
            PageSize = pageSize,
            Search = search
        }, cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,SalesManager")]
    public async Task<IActionResult> CreateChangeRequest([FromBody] CreateChangeRequestRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CreateChangeRequestCommand(_tenantService.GetCurrentTenantId(), request.OrderId, request.RequestedByUserId, request.RequestType, request.Description),
            cancellationToken);
        return CreatedAtAction(nameof(GetChangeRequests), result);
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Coordinator")]
    public async Task<IActionResult> ApproveChangeRequest(Guid id, [FromBody] HandleChangeRequestRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ApproveChangeRequestCommand(id, request.HandledByUserId, request.ResponseNote),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Coordinator")]
    public async Task<IActionResult> RejectChangeRequest(Guid id, [FromBody] HandleChangeRequestRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RejectChangeRequestCommand(id, request.HandledByUserId, request.ResponseNote),
            cancellationToken);
        return Ok(result);
    }
}
