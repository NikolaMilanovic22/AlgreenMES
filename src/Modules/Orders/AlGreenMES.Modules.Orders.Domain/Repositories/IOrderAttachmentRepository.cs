using AlGreenMES.Modules.Orders.Domain.Entities;

namespace AlGreenMES.Modules.Orders.Domain.Repositories;

/// <summary>Lightweight attachment location used for batch counting.</summary>
public record OrderAttachmentRef(Guid OrderId, Guid? OrderItemId);

public interface IOrderAttachmentRepository
{
    Task<IReadOnlyList<OrderAttachment>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderAttachment>> GetByOrderItemIdAsync(Guid orderItemId, CancellationToken cancellationToken = default);
    Task<OrderAttachment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> GetCountByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    // Batch-load attachment refs for many orders at once (avoids the tablet
    // queue/incoming N+1: one /attachments call per order card).
    Task<IReadOnlyList<OrderAttachmentRef>> GetRefsByOrderIdsAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default);
    Task<int> GetCountByOrderItemIdAsync(Guid orderItemId, CancellationToken cancellationToken = default);
    Task<bool> OrderItemBelongsToOrderAsync(Guid orderItemId, Guid orderId, CancellationToken cancellationToken = default);
    Task AddAsync(OrderAttachment attachment, CancellationToken cancellationToken = default);
    void Remove(OrderAttachment attachment);
    void RemoveRange(IEnumerable<OrderAttachment> attachments);
}
