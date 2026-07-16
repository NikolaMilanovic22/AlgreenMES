namespace AlGreenMES.Modules.Orders.Application.Interfaces;

public interface IWebPushService
{
    string GetVapidPublicKey();
    Task SubscribeAsync(Guid tenantId, Guid userId, string endpoint, string p256dhKey, string authKey, CancellationToken cancellationToken = default);
    Task UnsubscribeAsync(Guid userId, string endpoint, CancellationToken cancellationToken = default);
    Task SendToUserAsync(Guid userId, string title, string body, object? data = null, CancellationToken cancellationToken = default);
    Task SendToUsersAsync(IEnumerable<Guid> userIds, string title, string body, object? data = null, CancellationToken cancellationToken = default);
    Task SendToTenantAsync(Guid tenantId, string title, string body, object? data = null, CancellationToken cancellationToken = default);
}
