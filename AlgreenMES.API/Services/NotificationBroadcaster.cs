using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Orders.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AlgreenMES.API.Services;

public class NotificationBroadcaster : INotificationBroadcaster
{
    private readonly IHubContext<ProductionHub> _hubContext;

    public NotificationBroadcaster(IHubContext<ProductionHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task BroadcastNotificationCreatedAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.Group($"tenant-{tenantId}")
            .SendAsync("NotificationCreated", new { TenantId = tenantId }, cancellationToken);
    }
}
