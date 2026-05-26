using AlGreenMES.Modules.Identity.Api.Requests;
using AlGreenMES.Modules.Identity.Application.Commands.CreateShift;
using AlGreenMES.Modules.Identity.Application.Commands.UpdateShift;
using AlGreenMES.Modules.Identity.Application.Queries.GetShifts;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Identity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ShiftsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantService _tenantService;

    public ShiftsController(IMediator mediator, ITenantService tenantService)
    {
        _mediator = mediator;
        _tenantService = tenantService;
    }

    [HttpGet]
    public async Task<IActionResult> GetShifts(
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
        var result = await _mediator.Send(new GetShiftsQuery
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
    public async Task<IActionResult> CreateShift([FromBody] CreateShiftRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CreateShiftCommand(
                _tenantService.GetCurrentTenantId(),
                request.Name,
                request.StartTime,
                request.EndTime,
                request.BreakMinutes,
                request.MaxOvertimeHours,
                request.AutoLogoutAfterHours,
                request.AlarmBeforeLogoutMinutes),
            cancellationToken);

        return Created($"/api/shifts/{result.Id}", result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> UpdateShift(Guid id, [FromBody] UpdateShiftRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateShiftCommand(
                id,
                request.Name,
                request.StartTime,
                request.EndTime,
                request.IsActive,
                request.BreakMinutes,
                request.MaxOvertimeHours,
                request.AutoLogoutAfterHours,
                request.AlarmBeforeLogoutMinutes),
            cancellationToken);

        return Ok(result);
    }
}
