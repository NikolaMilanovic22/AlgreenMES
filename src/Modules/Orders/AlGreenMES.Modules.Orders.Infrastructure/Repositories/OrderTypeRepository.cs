using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Orders.Domain.Entities.OrderTypes;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Orders.Infrastructure.Repositories;

public class OrderTypeRepository : IOrderTypeRepository
{
    private readonly OrdersDbContext _dbContext;

    public OrderTypeRepository(OrdersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<OrderType?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.OrderTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<OrderType?> GetByCodeAsync(string code, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return await _dbContext.OrderTypes
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Code == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderType>> GetByTenantIdAsync(Guid tenantId, bool activeOnly, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.OrderTypes.Where(t => t.TenantId == tenantId);
        if (activeOnly) query = query.Where(t => t.IsActive);
        return await query.OrderBy(t => t.Code).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(OrderType orderType, CancellationToken cancellationToken = default)
    {
        await _dbContext.OrderTypes.AddAsync(orderType, cancellationToken);
    }

    public void Remove(OrderType orderType)
    {
        _dbContext.OrderTypes.Remove(orderType);
    }

    public async Task<bool> ExistsByCodeAsync(string code, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return await _dbContext.OrderTypes
            .AnyAsync(t => t.TenantId == tenantId && t.Code == normalized, cancellationToken);
    }

    public async Task<bool> IsInUseAsync(Guid orderTypeId, CancellationToken cancellationToken = default)
    {
        // Order.OrderType is a free-form string code referencing OrderType.Code
        // (Saša 20.06.2026: admins can create custom types beyond the original
        // 4-enum set). Direct string comparison — no enum round-trip needed.
        var type = await _dbContext.OrderTypes
            .Where(t => t.Id == orderTypeId)
            .Select(t => new { t.Code, t.TenantId })
            .FirstOrDefaultAsync(cancellationToken);
        if (type is null) return false;

        return await _dbContext.Orders
            .AnyAsync(o => o.TenantId == type.TenantId && o.OrderType == type.Code, cancellationToken);
    }

    public async Task<PagedResult<OrderType>> GetPagedAsync(Guid tenantId, bool? isActive, string? search, DateTime? createdFrom, DateTime? createdTo, string? sortBy, bool isDescending, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.OrderTypes.Where(t => t.TenantId == tenantId);

        if (isActive.HasValue)
            query = query.Where(t => t.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(t => t.Name.ToLower().Contains(s) || t.Code.ToLower().Contains(s));
        }

        if (createdFrom.HasValue)
        {
            var from = DateTime.SpecifyKind(createdFrom.Value.Date, DateTimeKind.Utc);
            query = query.Where(x => x.CreatedAt >= from);
        }
        if (createdTo.HasValue)
        {
            var to = DateTime.SpecifyKind(createdTo.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(x => x.CreatedAt < to);
        }

        IOrderedQueryable<OrderType> sorted = sortBy?.ToLowerInvariant() switch
        {
            "name" => isDescending ? query.OrderByDescending(t => t.Name) : query.OrderBy(t => t.Name),
            "createdat" => isDescending ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt),
            _ => isDescending ? query.OrderByDescending(t => t.Code) : query.OrderBy(t => t.Code),
        };

        return await sorted.ToPagedResultAsync(page, pageSize, cancellationToken);
    }
}
