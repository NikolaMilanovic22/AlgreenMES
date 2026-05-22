using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.GetOrderTypes;

public class GetOrderTypesQueryHandler : IRequestHandler<GetOrderTypesQuery, PagedResult<OrderTypeDto>>
{
    private readonly IOrderTypeRepository _repository;

    public GetOrderTypesQueryHandler(IOrderTypeRepository repository)
    {
        _repository = repository;
    }

    public async Task<PagedResult<OrderTypeDto>> Handle(GetOrderTypesQuery request, CancellationToken cancellationToken)
    {
        var result = await _repository.GetPagedAsync(
            request.TenantId, request.IsActive, request.Search,
            request.GetCreatedFromUtc(), request.GetCreatedToUtc(),
            request.SortBy, request.IsDescending,
            request.GetPage(), request.GetPageSize(), cancellationToken);

        return result.MapItems(t => t.Adapt<OrderTypeDto>());
    }
}
