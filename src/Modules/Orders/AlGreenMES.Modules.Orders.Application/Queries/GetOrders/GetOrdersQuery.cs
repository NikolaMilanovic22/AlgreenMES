using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Domain.Enums;

namespace AlGreenMES.Modules.Orders.Application.Queries.GetOrders;

public record GetOrdersQuery : PagedQuery<PagedResult<OrderDto>>
{
    public Guid TenantId { get; init; }
    public OrderStatus? Status { get; init; }
    public string? OrderType { get; init; }
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
}
