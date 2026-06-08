using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Production.Application.DTOs;
using AlGreenMES.Modules.Production.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Queries.GetIstorija;

public class GetIstorijaQueryHandler : IRequestHandler<GetIstorijaQuery, PagedResult<StockMovementDto>>
{
    private readonly IStockMovementRepository _stockRepo;

    public GetIstorijaQueryHandler(IStockMovementRepository stockRepo)
    {
        _stockRepo = stockRepo;
    }

    public async Task<PagedResult<StockMovementDto>> Handle(GetIstorijaQuery request, CancellationToken cancellationToken)
    {
        var page = await _stockRepo.GetPagedAsync(
            request.TenantId, request.Type, request.MaterialId, request.DocRef,
            request.From, request.To, request.Page, request.PageSize,
            request.SortBy, request.SortDirection, cancellationToken);

        return page.MapItems(s => new StockMovementDto(
            s.Id, s.MaterialId, s.Material.Code, s.Material.Name, s.Material.Unit,
            s.Material.Category, s.Material.DimensionX, s.Material.DimensionY, s.Material.DimensionZ,
            s.Type, s.Quantity, s.UnitPrice, s.TotalPrice,
            s.MovementDate, s.DocumentReference, s.Notes, s.CreatedAt));
    }
}
