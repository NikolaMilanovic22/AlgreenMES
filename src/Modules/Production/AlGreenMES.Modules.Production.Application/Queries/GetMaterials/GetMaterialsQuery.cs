using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Production.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Queries.GetMaterials;

public record GetMaterialsQuery(
    Guid TenantId,
    bool? IsActive,
    string? Category,
    string? Search,
    string? SortBy,
    bool IsDescending,
    int Page,
    int PageSize) : IRequest<PagedResult<MaterialDto>>;
