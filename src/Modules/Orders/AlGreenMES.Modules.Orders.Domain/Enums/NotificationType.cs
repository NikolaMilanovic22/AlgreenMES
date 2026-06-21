namespace AlGreenMES.Modules.Orders.Domain.Enums;

public enum NotificationType
{
    DeadlineWarning,
    DeadlineCritical,
    BlockRequest,
    BlockRequestApproved,
    BlockRequestRejected,
    ProcessCompleted,
    ProcessBlocked,
    OrderActivated,
    WorkerAutoLoggedOut,
    MaterialLowStock,
    ChangeRequest,
    ChangeRequestApproved,
    ChangeRequestRejected,
    // Saša 18.06.2026: daily nudge for tenant Admins. Two distinct
    // types so the FE bell can color them differently (warning vs error)
    // — same template family, different visual urgency.
    SubscriptionExpiring,
    SubscriptionExpired,
}
