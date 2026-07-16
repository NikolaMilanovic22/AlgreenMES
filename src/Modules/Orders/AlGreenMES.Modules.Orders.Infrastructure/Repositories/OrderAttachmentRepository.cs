using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Orders.Infrastructure.Repositories;

public class OrderAttachmentRepository : IOrderAttachmentRepository
{
    private readonly OrdersDbContext _dbContext;

    public OrderAttachmentRepository(OrdersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<OrderAttachment>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.OrderAttachments
            .Where(a => a.OrderId == orderId)
            .OrderBy(a => a.UploadedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OrderAttachment>> GetByOrderItemIdAsync(Guid orderItemId, CancellationToken cancellationToken = default)
        => await _dbContext.OrderAttachments
            .Where(a => a.OrderItemId == orderItemId)
            .OrderBy(a => a.UploadedAt)
            .ToListAsync(cancellationToken);

    public async Task<OrderAttachment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _dbContext.OrderAttachments
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<int> GetCountByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.OrderAttachments
            .CountAsync(a => a.OrderId == orderId && a.OrderItemId == null, cancellationToken);

    public async Task<IReadOnlyList<OrderAttachmentRef>> GetRefsByOrderIdsAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0) return [];
        return await _dbContext.OrderAttachments
            .Where(a => orderIds.Contains(a.OrderId))
            .Select(a => new OrderAttachmentRef(a.OrderId, a.OrderItemId))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountByOrderItemIdAsync(Guid orderItemId, CancellationToken cancellationToken = default)
        => await _dbContext.OrderAttachments
            .CountAsync(a => a.OrderItemId == orderItemId, cancellationToken);

    public async Task<bool> OrderItemBelongsToOrderAsync(Guid orderItemId, Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.OrderItems
            .AnyAsync(i => i.Id == orderItemId && i.OrderId == orderId, cancellationToken);

    public async Task AddAsync(OrderAttachment attachment, CancellationToken cancellationToken = default)
        => await _dbContext.OrderAttachments.AddAsync(attachment, cancellationToken);

    public void Remove(OrderAttachment attachment)
        => _dbContext.OrderAttachments.Remove(attachment);

    public void RemoveRange(IEnumerable<OrderAttachment> attachments)
        => _dbContext.OrderAttachments.RemoveRange(attachments);
}
