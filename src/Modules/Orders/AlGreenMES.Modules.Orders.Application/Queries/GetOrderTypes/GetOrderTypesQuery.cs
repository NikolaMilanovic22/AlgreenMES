using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Orders.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.GetOrderTypes;

public record GetOrderTypesQuery : PagedQuery<PagedResult<OrderTypeDto>>
{
    public Guid TenantId { get; init; }
    public bool? IsActive { get; init; }
}
