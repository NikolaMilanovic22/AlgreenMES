using AlGreenMES.Modules.Production.Api.Requests;
using AlGreenMES.Modules.Production.Application.Commands.CreateMaterial;
using AlGreenMES.Modules.Production.Application.Commands.ImportMaterials;
using AlGreenMES.Modules.Production.Application.Commands.SetMaterialActive;
using AlGreenMES.Modules.Production.Application.Commands.UpdateMaterial;
using AlGreenMES.Modules.Production.Application.Queries.GetMaterial;
using AlGreenMES.Modules.Production.Application.Queries.GetMaterials;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AlGreenMES.BuildingBlocks.Common.Authorization;

namespace AlGreenMES.Modules.Production.Api.Controllers;

[ApiController]
[Route("api/materials")]
[Authorize]
public class MaterialsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantService _tenantService;
    private readonly ICurrentUserService _currentUser;

    public MaterialsController(IMediator mediator, ITenantService tenantService, ICurrentUserService currentUser)
    {
        _mediator = mediator;
        _tenantService = tenantService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Roles = RoleGroups.ProductionFloor)]
    public async Task<IActionResult> GetMaterials(
        [FromQuery] bool? isActive,
        [FromQuery] string? category,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] bool isDescending = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetMaterialsQuery(_tenantService.GetCurrentTenantId(), isActive, category, search, sortBy, isDescending, page, pageSize),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = RoleGroups.ProductionFloor)]
    public async Task<IActionResult> GetMaterial(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMaterialQuery(id), cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = RoleGroups.ManagerOrWarehouse)]
    public async Task<IActionResult> CreateMaterial([FromBody] CreateMaterialRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateMaterialCommand(
            _tenantService.GetCurrentTenantId(),
            request.Code, request.Name, request.Unit, request.Category,
            request.MinQuantity, request.MaxQuantity,
            request.DimensionX, request.DimensionY, request.DimensionZ,
            request.Location, request.Notes,
            _currentUser.GetCurrentUserId()), cancellationToken);
        return CreatedAtAction(nameof(GetMaterial), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = RoleGroups.ManagerOrWarehouse)]
    public async Task<IActionResult> UpdateMaterial(Guid id, [FromBody] UpdateMaterialRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateMaterialCommand(
            id, request.Name, request.Unit, request.Category,
            request.MinQuantity, request.MaxQuantity,
            request.DimensionX, request.DimensionY, request.DimensionZ,
            request.Location, request.Notes,
            _currentUser.GetCurrentUserId()), cancellationToken);
        return Ok(result);
    }

    [HttpPost("import")]
    [Authorize(Roles = RoleGroups.ManagerOrWarehouse)]
    public async Task<IActionResult> Import([FromBody] ImportMaterialsRequest request, CancellationToken cancellationToken)
    {
        var items = request.Items
            .Select(i => new ImportMaterialItem(
                i.Code, i.Name, i.Unit, i.Category,
                i.MinQuantity, i.MaxQuantity,
                i.DimensionX, i.DimensionY, i.DimensionZ,
                i.Location, i.Notes))
            .ToList();
        var result = await _mediator.Send(
            new ImportMaterialsCommand(_tenantService.GetCurrentTenantId(), items, _currentUser.GetCurrentUserId()),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Roles = RoleGroups.ManagerOrWarehouse)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new SetMaterialActiveCommand(id, true), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Roles = RoleGroups.ManagerOrWarehouse)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new SetMaterialActiveCommand(id, false), cancellationToken);
        return NoContent();
    }
}
