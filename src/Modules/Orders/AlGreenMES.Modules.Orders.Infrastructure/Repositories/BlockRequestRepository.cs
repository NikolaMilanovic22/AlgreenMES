using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Orders.Infrastructure.Repositories;

public class BlockRequestRepository : IBlockRequestRepository
{
    private readonly OrdersDbContext _dbContext;

    public BlockRequestRepository(OrdersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BlockRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.BlockRequests
            .FirstOrDefaultAsync(br => br.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<BlockRequest>> GetByTenantIdAsync(Guid tenantId, RequestStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.BlockRequests
            .Where(br => br.TenantId == tenantId);

        if (status.HasValue)
            query = query.Where(br => br.Status == status.Value);

        return await query
            .OrderByDescending(br => br.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(BlockRequest request, CancellationToken cancellationToken = default)
    {
        await _dbContext.BlockRequests.AddAsync(request, cancellationToken);
    }

    public async Task<PagedResult<BlockRequest>> GetPagedAsync(Guid tenantId, RequestStatus? status, Guid? orderId, string? search, DateTime? createdFrom, DateTime? createdTo, string? sortBy, bool isDescending, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.BlockRequests
            .Include(br => br.OrderItemProcess)
                .ThenInclude(p => p!.OrderItem)
                    .ThenInclude(i => i.Order)
            .Where(br => br.TenantId == tenantId);

        if (status.HasValue)
            query = query.Where(br => br.Status == status.Value);

        if (orderId.HasValue)
            query = query.Where(br => br.OrderItemProcess != null && br.OrderItemProcess.OrderItem.OrderId == orderId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(br =>
                (br.RequestNote != null && br.RequestNote.ToLower().Contains(s)) ||
                (br.OrderItemProcess != null && br.OrderItemProcess.OrderItem.Order.OrderNumber.ToLower().Contains(s)));
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

        IOrderedQueryable<BlockRequest> sorted;
        switch (sortBy?.ToLowerInvariant())
        {
            case "status":
                sorted = isDescending ? query.OrderByDescending(br => br.Status) : query.OrderBy(br => br.Status);
                break;
            case "requestnote":
                sorted = isDescending ? query.OrderByDescending(br => br.RequestNote) : query.OrderBy(br => br.RequestNote);
                break;
            case "updatedat":
                sorted = isDescending ? query.OrderByDescending(br => br.UpdatedAt) : query.OrderBy(br => br.UpdatedAt);
                break;
            case "handledat":
                sorted = isDescending ? query.OrderByDescending(br => br.HandledAt) : query.OrderBy(br => br.HandledAt);
                break;
            default: // createdAt desc
                sorted = isDescending ? query.OrderByDescending(br => br.CreatedAt) : query.OrderBy(br => br.CreatedAt);
                break;
        }

        return await sorted.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<IReadOnlyList<BlockRequest>> GetApprovedByProcessIdAsync(Guid orderItemProcessId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.BlockRequests
            .Where(br => br.OrderItemProcessId == orderItemProcessId && br.Status == RequestStatus.Approved)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BlockRequest>> GetPendingByProcessIdAsync(Guid orderItemProcessId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.BlockRequests
            .Where(br => br.OrderItemProcessId == orderItemProcessId && br.Status == RequestStatus.Pending)
            .ToListAsync(cancellationToken);
    }
}
