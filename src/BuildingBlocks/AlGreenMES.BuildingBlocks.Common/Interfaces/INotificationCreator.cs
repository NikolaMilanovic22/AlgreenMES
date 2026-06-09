namespace AlGreenMES.BuildingBlocks.Common.Interfaces;

/// <summary>
/// Cross-module entry point for emitting in-app notifications. Implemented in
/// the Orders module (the owner of the Notification entity). Other modules
/// (e.g. Production) depend on this interface so they can fan out
/// notifications without referencing Orders types directly.
///
/// Caller passes the NotificationType as an int because the enum lives in the
/// Orders.Domain assembly that other modules must not reference. The Orders
/// implementation casts the int back to its NotificationType enum.
/// </summary>
public interface INotificationCreator
{
    /// <summary>
    /// Persist one notification per management user (Coordinator / Manager /
    /// Admin / SuperAdmin) of the given tenant. Roles that don't have a
    /// recipient simply produce zero rows for that role.
    /// </summary>
    /// <param name="tenantId">Tenant scope.</param>
    /// <param name="notificationTypeValue">Value of the NotificationType enum.</param>
    /// <param name="title">User-facing title shown in the bell list.</param>
    /// <param name="message">Plain-text body.</param>
    /// <param name="referenceType">Optional opaque tag (e.g. "Material").</param>
    /// <param name="referenceId">Optional record id for click navigation.</param>
    Task NotifyManagementAsync(
        Guid tenantId,
        int notificationTypeValue,
        string title,
        string message,
        string? referenceType = null,
        Guid? referenceId = null,
        string? paramsJson = null,
        CancellationToken cancellationToken = default);
}
