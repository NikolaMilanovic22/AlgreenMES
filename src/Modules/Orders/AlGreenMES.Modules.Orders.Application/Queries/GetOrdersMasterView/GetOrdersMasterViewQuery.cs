using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Domain.Enums;

namespace AlGreenMES.Modules.Orders.Application.Queries.GetOrdersMasterView;

public record GetOrdersMasterViewQuery : PagedQuery<PagedResult<OrderMasterViewDto>>
{
    public Guid TenantId { get; init; }
    public OrderStatus? Status { get; init; }
    public OrderType? OrderType { get; init; }
    public bool? IsInvoiced { get; init; }
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
}
