using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Production.Application.DTOs;
using AlGreenMES.Modules.Production.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Queries.GetMaterials;

public class GetMaterialsQueryHandler : IRequestHandler<GetMaterialsQuery, PagedResult<MaterialDto>>
{
    private readonly IMaterialRepository _repo;

    public GetMaterialsQueryHandler(IMaterialRepository repo)
    {
        _repo = repo;
    }

    public async Task<PagedResult<MaterialDto>> Handle(GetMaterialsQuery request, CancellationToken cancellationToken)
    {
        var page = await _repo.GetPagedAsync(
            request.TenantId, request.IsActive, request.Category, request.Search,
            request.SortBy, request.IsDescending, request.Page, request.PageSize, cancellationToken);

        return page.MapItems(m => m.Adapt<MaterialDto>());
    }
}
