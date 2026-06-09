using AlGreenMES.Modules.Production.Api.Requests;
using AlGreenMES.Modules.Production.Application.Commands.CreateStockEntry;
using AlGreenMES.Modules.Production.Application.Queries.GetStockHistory;
using AlGreenMES.Modules.Production.Application.Queries.GetStockBalances;
using AlGreenMES.Modules.Production.Domain.Enums;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Production.Api.Controllers;

/// <summary>
/// Magacin (warehouse) endpoints — Stanje (current stock), Ulaz/Izlaz
/// (stock entries), Istorija (transaction log). Saša 08.06.2026 Excel
/// spec. Magacioner role + management roles can read; only management
/// + Magacioner can post stock entries.
/// </summary>
[ApiController]
[Route("api/warehouse")]
[Authorize]
public class WarehouseController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantService _tenantService;
    private readonly ICurrentUserService _currentUser;

    public WarehouseController(IMediator mediator, ITenantService tenantService, ICurrentUserService currentUser)
    {
        _mediator = mediator;
        _tenantService = tenantService;
        _currentUser = currentUser;
    }

    [HttpGet("stock")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Coordinator,Magacioner")]
    public async Task<IActionResult> GetStockBalances(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetStockBalancesQuery(_tenantService.GetCurrentTenantId()), cancellationToken);
        return Ok(result);
    }

    [HttpGet("history")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Coordinator,Magacioner")]
    public async Task<IActionResult> GetStockHistory(
        [FromQuery] StockMovementType? type,
        [FromQuery] Guid? materialId,
        [FromQuery] string? docRef,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        [FromQuery] string? category = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetStockHistoryQuery(_tenantService.GetCurrentTenantId(), type, materialId, docRef, from, to, page, pageSize, sortBy, sortDirection, category),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("entries")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Magacioner")]
    public async Task<IActionResult> CreateEntry([FromBody] CreateStockEntryRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateStockEntryCommand(
            _tenantService.GetCurrentTenantId(),
            request.Type,
            request.DocumentReference,
            request.MovementDate,
            request.Notes,
            request.Lines.Select(l => new StockEntryLine(l.MaterialId, l.Quantity, l.UnitPrice, l.Notes)).ToList(),
            _currentUser.GetCurrentUserId()), cancellationToken);
        return Ok(result);
    }
}
