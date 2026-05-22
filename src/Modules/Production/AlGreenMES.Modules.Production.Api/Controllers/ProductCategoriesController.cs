using AlGreenMES.Modules.Production.Api.Requests;
using AlGreenMES.Modules.Production.Application.Commands.ActivateProductCategory;
using AlGreenMES.Modules.Production.Application.Commands.AddCategoryDependency;
using AlGreenMES.Modules.Production.Application.Commands.AddCategoryProcess;
using AlGreenMES.Modules.Production.Application.Commands.CreateProductCategory;
using AlGreenMES.Modules.Production.Application.Commands.DeactivateProductCategory;
using AlGreenMES.Modules.Production.Application.Commands.DeleteProductCategory;
using AlGreenMES.Modules.Production.Application.Commands.RemoveCategoryDependency;
using AlGreenMES.Modules.Production.Application.Commands.RemoveCategoryProcess;
using AlGreenMES.Modules.Production.Application.Commands.UpdateProductCategory;
using AlGreenMES.Modules.Production.Application.Queries.GetProductCategories;
using AlGreenMES.Modules.Production.Application.Queries.GetProductCategoryById;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Production.Api.Controllers;

[ApiController]
[Route("api/product-categories")]
[Authorize]
public class ProductCategoriesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantService _tenantService;

    public ProductCategoriesController(IMediator mediator, ITenantService tenantService)
    {
        _mediator = mediator;
        _tenantService = tenantService;
    }

    [HttpGet]
    public async Task<IActionResult> GetProductCategories(
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
        var result = await _mediator.Send(new GetProductCategoriesQuery
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
    public async Task<IActionResult> GetProductCategoryById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetProductCategoryByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> CreateProductCategory([FromBody] CreateProductCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CreateProductCategoryCommand(
                _tenantService.GetCurrentTenantId(), request.Name, request.Description, null,
                request.DefaultWarningDays, request.DefaultCriticalDays,
                request.Processes?.Select(p => new ProcessInput(p.ProcessId, p.SequenceOrder, p.DefaultComplexity)).ToList(),
                request.Dependencies?.Select(d => new DependencyInput(d.ProcessId, d.DependsOnProcessId)).ToList()),
            cancellationToken);
        return CreatedAtAction(nameof(GetProductCategoryById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> UpdateProductCategory(Guid id, [FromBody] UpdateProductCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateProductCategoryCommand(
                id, request.Name, request.Description,
                request.DefaultWarningDays, request.DefaultCriticalDays,
                request.Processes?.Select(p => new ProcessInput(p.ProcessId, p.SequenceOrder, p.DefaultComplexity)).ToList(),
                request.Dependencies?.Select(d => new DependencyInput(d.ProcessId, d.DependsOnProcessId)).ToList()),
            cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> DeleteProductCategory(Guid id, [FromQuery] bool forceDeactivate = false, [FromQuery] bool forceDelete = false, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new DeleteProductCategoryCommand(id, forceDeactivate, forceDelete), cancellationToken);
        if (!result.HardDeleted && !result.Deactivated)
            return Ok(new { hasReferences = true, referencedOrderCount = result.ReferencedOrderCount });
        return NoContent();
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> ActivateProductCategory(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ActivateProductCategoryCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{categoryId:guid}/processes")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> AddCategoryProcess(Guid categoryId, [FromBody] AddCategoryProcessRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new AddCategoryProcessCommand(categoryId, request.ProcessId, request.SequenceOrder, request.DefaultComplexity),
            cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{categoryId:guid}/processes/{processId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> RemoveCategoryProcess(Guid categoryId, Guid processId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RemoveCategoryProcessCommand(categoryId, processId),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("{categoryId:guid}/dependencies")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> AddCategoryDependency(Guid categoryId, [FromBody] AddCategoryDependencyRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new AddCategoryDependencyCommand(categoryId, request.ProcessId, request.DependsOnProcessId),
            cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{categoryId:guid}/dependencies/{dependencyId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> RemoveCategoryDependency(Guid categoryId, Guid dependencyId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RemoveCategoryDependencyCommand(categoryId, dependencyId),
            cancellationToken);
        return Ok(result);
    }
}
