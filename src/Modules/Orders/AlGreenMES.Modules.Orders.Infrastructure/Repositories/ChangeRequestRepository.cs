using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Orders.Infrastructure.Repositories;

public class ChangeRequestRepository : IChangeRequestRepository
{
    private readonly OrdersDbContext _dbContext;

    public ChangeRequestRepository(OrdersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ChangeRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ChangeRequests
            .FirstOrDefaultAsync(cr => cr.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ChangeRequest>> GetByTenantIdAsync(Guid tenantId, RequestStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ChangeRequests
            .Where(cr => cr.TenantId == tenantId);

        if (status.HasValue)
            query = query.Where(cr => cr.Status == status.Value);

        return await query
            .OrderByDescending(cr => cr.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChangeRequest>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ChangeRequests
            .Where(cr => cr.RequestedByUserId == userId)
            .OrderByDescending(cr => cr.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(ChangeRequest request, CancellationToken cancellationToken = default)
    {
        await _dbContext.ChangeRequests.AddAsync(request, cancellationToken);
    }

    public async Task<PagedResult<ChangeRequest>> GetPagedAsync(Guid tenantId, RequestStatus? status, ChangeRequestType? requestType, Guid? orderId, string? search, DateTime? createdFrom, DateTime? createdTo, string? sortBy, bool isDescending, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ChangeRequests
            .Include(cr => cr.Order)
            .Where(cr => cr.TenantId == tenantId);

        if (status.HasValue)
            query = query.Where(cr => cr.Status == status.Value);

        if (requestType.HasValue)
            query = query.Where(cr => cr.RequestType == requestType.Value);

        if (orderId.HasValue)
            query = query.Where(cr => cr.OrderId == orderId.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(cr =>
                cr.Description.ToLower().Contains(search.ToLower()) ||
                cr.Order.OrderNumber.ToLower().Contains(search.ToLower()));

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

        IOrderedQueryable<ChangeRequest> sorted;
        switch (sortBy?.ToLowerInvariant())
        {
            case "description":
                sorted = isDescending ? query.OrderByDescending(cr => cr.Description) : query.OrderBy(cr => cr.Description);
                break;
            case "updatedat":
                sorted = isDescending ? query.OrderByDescending(cr => cr.UpdatedAt) : query.OrderBy(cr => cr.UpdatedAt);
                break;
            case "handledat":
                sorted = isDescending ? query.OrderByDescending(cr => cr.HandledAt) : query.OrderBy(cr => cr.HandledAt);
                break;
            default: // createdAt desc
                sorted = isDescending ? query.OrderByDescending(cr => cr.CreatedAt) : query.OrderBy(cr => cr.CreatedAt);
                break;
        }

        return await sorted.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<PagedResult<ChangeRequest>> GetPagedByUserAsync(Guid tenantId, Guid userId, RequestStatus? status, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ChangeRequests
            .Include(cr => cr.Order)
            .Where(cr => cr.TenantId == tenantId && cr.RequestedByUserId == userId);

        if (status.HasValue)
            query = query.Where(cr => cr.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(cr =>
                cr.Description.ToLower().Contains(search.ToLower()) ||
                cr.Order.OrderNumber.ToLower().Contains(search.ToLower()));

        query = query.OrderByDescending(cr => cr.CreatedAt);

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }
}
