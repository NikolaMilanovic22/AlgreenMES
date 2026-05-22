using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;

namespace AlGreenMES.Modules.Orders.Domain.Repositories;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Order?> GetByIdWithItemsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Order?> GetByIdWithFullDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetByTenantIdAsync(Guid tenantId, OrderStatus? status = null, CancellationToken cancellationToken = default);
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    void AddItem(OrderItem item);
    Task<bool> ExistsByOrderNumberAsync(string orderNumber, Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetActiveOrdersWithProcessesAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<PagedResult<Order>> GetPagedAsync(Guid tenantId, OrderStatus? status, OrderType? orderType, DateTime? dateFrom, DateTime? dateTo, string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<PagedResult<Order>> GetPagedWithProcessesAsync(Guid tenantId, OrderStatus? status, OrderType? orderType, bool? isInvoiced, DateTime? dateFrom, DateTime? dateTo, string? search, string? sortBy, bool isDescending, int page, int pageSize, CancellationToken cancellationToken = default);
}
