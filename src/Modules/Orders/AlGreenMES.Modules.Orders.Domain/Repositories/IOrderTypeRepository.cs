using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Orders.Domain.Entities.OrderTypes;

namespace AlGreenMES.Modules.Orders.Domain.Repositories;

public interface IOrderTypeRepository
{
    Task<OrderType?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OrderType?> GetByCodeAsync(string code, Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderType>> GetByTenantIdAsync(Guid tenantId, bool activeOnly, CancellationToken cancellationToken = default);
    Task AddAsync(OrderType orderType, CancellationToken cancellationToken = default);
    void Remove(OrderType orderType);
    Task<bool> ExistsByCodeAsync(string code, Guid tenantId, CancellationToken cancellationToken = default);
    Task<bool> IsInUseAsync(Guid orderTypeId, CancellationToken cancellationToken = default);
    Task<PagedResult<OrderType>> GetPagedAsync(Guid tenantId, bool? isActive, string? search, DateTime? createdFrom, DateTime? createdTo, string? sortBy, bool isDescending, int page, int pageSize, CancellationToken cancellationToken = default);
}
