using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;

namespace AlGreenMES.Modules.Orders.Infrastructure.Services;

/// <summary>
/// Cross-module notification emitter. Implements <see cref="INotificationCreator"/>
/// so other modules (Production for low-stock alerts, etc.) can fan out
/// in-app notifications to management users without referencing
/// Orders types directly.
/// </summary>
public class NotificationCreator : INotificationCreator
{
    private static readonly UserRole[] ManagementRoles =
    {
        UserRole.SuperAdmin,
        UserRole.Admin,
        UserRole.Manager,
        UserRole.Coordinator,
    };

    private readonly INotificationRepository _notificationRepo;
    private readonly IUserRepository _userRepo;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly INotificationBroadcaster _broadcaster;

    public NotificationCreator(
        INotificationRepository notificationRepo,
        IUserRepository userRepo,
        IOrdersUnitOfWork unitOfWork,
        INotificationBroadcaster broadcaster)
    {
        _notificationRepo = notificationRepo;
        _userRepo = userRepo;
        _unitOfWork = unitOfWork;
        _broadcaster = broadcaster;
    }

    public async Task NotifyManagementAsync(
        Guid tenantId,
        int notificationTypeValue,
        string title,
        string message,
        string? referenceType = null,
        Guid? referenceId = null,
        string? paramsJson = null,
        CancellationToken cancellationToken = default)
    {
        var type = (NotificationType)notificationTypeValue;
        var users = await _userRepo.GetByTenantIdAsync(tenantId, cancellationToken);
        var recipients = users
            .Where(u => u.IsActive && ManagementRoles.Contains(u.Role))
            .Select(u => u.Id)
            .ToList();

        if (recipients.Count == 0) return;

        foreach (var userId in recipients)
        {
            var n = Notification.Create(tenantId, userId, type, title, message, referenceType, referenceId, paramsJson);
            await _notificationRepo.AddAsync(n, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Push the FE so the bell badge refreshes within ~1s instead of waiting
        // for the polling tick. Anyone in the tenant group will hear it; clients
        // who shouldn't see the notification just invalidate an empty cache.
        await _broadcaster.BroadcastNotificationCreatedAsync(tenantId, cancellationToken);
    }
}
