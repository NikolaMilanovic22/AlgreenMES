namespace AlGreenMES.BuildingBlocks.Common.Interfaces;

/// <summary>
/// Cross-module SignalR broadcast for "a new in-app notification was just written
/// for someone in this tenant — invalidate any client-side notification caches."
///
/// Used by <see cref="INotificationCreator"/> after persisting notifications, so
/// the bell badge on the dashboard updates within ~1s instead of waiting for the
/// polling tick. Lives in BuildingBlocks.Common so modules that write
/// notifications (Orders for block requests, Production for low-stock, etc.)
/// can call it without referencing the SignalR hub directly.
///
/// Implemented in the API composition root (AlgreenMES.API) where IHubContext is
/// available.
/// </summary>
public interface INotificationBroadcaster
{
    Task BroadcastNotificationCreatedAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
