using AlGreenMES.Modules.Orders.Domain.Repositories;

namespace AlGreenMES.Modules.Orders.Application.Queries.Tablet;

/// <summary>
/// Precomputes attachment counts per queue/incoming item from a single batched
/// load, so the tablet doesn't fire one /attachments request per order card
/// (the N+1 Sentry flagged on /queue). Mirrors the FE AttachmentIndicator:
/// order-level attachments (OrderItemId == null) count toward every item of
/// that order, plus the item's own attachments.
/// </summary>
public sealed class AttachmentCountLookup
{
    private readonly Dictionary<Guid, int> _orderLevel;
    private readonly Dictionary<Guid, int> _itemLevel;

    private AttachmentCountLookup(Dictionary<Guid, int> orderLevel, Dictionary<Guid, int> itemLevel)
    {
        _orderLevel = orderLevel;
        _itemLevel = itemLevel;
    }

    public static AttachmentCountLookup Build(IReadOnlyList<OrderAttachmentRef> refs)
    {
        var orderLevel = refs
            .Where(r => r.OrderItemId == null)
            .GroupBy(r => r.OrderId)
            .ToDictionary(g => g.Key, g => g.Count());
        var itemLevel = refs
            .Where(r => r.OrderItemId != null)
            .GroupBy(r => r.OrderItemId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        return new AttachmentCountLookup(orderLevel, itemLevel);
    }

    public int CountFor(Guid orderId, Guid orderItemId)
        => _orderLevel.GetValueOrDefault(orderId) + _itemLevel.GetValueOrDefault(orderItemId);
}
