namespace AlGreenMES.Modules.Orders.Application.DTOs.Events;

// Bojan 30.05.2026 — fired when a worker's session is auto-closed (either by
// the tablet hitting its cap or the server-side lazy safety net). The event
// service broadcasts to the tenant SignalR group AND persists a Notification
// per dashboard user (Admin/Manager/Coordinator/SalesManager) so coordinators
// see "Auto-odjava: <radnik>" in their notification list.
public record WorkerAutoLoggedOutEvent(
    Guid UserId,
    Guid SessionId,
    DateTime AutoLoggedOutAt,
    int? DurationMinutes,
    Guid TenantId);
