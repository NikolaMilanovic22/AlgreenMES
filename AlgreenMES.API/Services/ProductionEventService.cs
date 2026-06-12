using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using AlGreenMES.Modules.Orders.Api.Hubs;
using AlGreenMES.Modules.Orders.Application.DTOs.Events;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using Microsoft.AspNetCore.SignalR;

namespace AlgreenMES.API.Services;

public class ProductionEventService : IProductionEventService
{
    private readonly IHubContext<ProductionHub> _hubContext;
    private readonly IWebPushService _webPushService;
    private readonly IUserRepository _userRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly INotificationBroadcaster _notificationBroadcaster;

    private static readonly UserRole[] DashboardRoles = [UserRole.Admin, UserRole.Manager, UserRole.Coordinator, UserRole.SalesManager];

    public ProductionEventService(
        IHubContext<ProductionHub> hubContext,
        IWebPushService webPushService,
        IUserRepository userRepository,
        INotificationRepository notificationRepository,
        IOrdersUnitOfWork unitOfWork,
        INotificationBroadcaster notificationBroadcaster)
    {
        _hubContext = hubContext;
        _webPushService = webPushService;
        _userRepository = userRepository;
        _notificationRepository = notificationRepository;
        _unitOfWork = unitOfWork;
        _notificationBroadcaster = notificationBroadcaster;
    }

    public async Task NotifyOrderActivatedAsync(OrderActivatedEvent evt, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"tenant-{evt.TenantId}")
            .SendAsync("OrderActivated", evt, cancellationToken);

        var title = "Nova narudžbina";
        var message = $"Narudžbina #{evt.OrderNumber} je aktivirana";

        await _webPushService.SendToTenantAsync(evt.TenantId, title, message,
            new { type = "OrderActivated", evt.OrderId, evt.OrderNumber },
            cancellationToken);

        // Create in-app notifications for ALL active users (dashboard + workers)
        await CreateNotificationsForAllUsersAsync(evt.TenantId,
            NotificationType.OrderActivated, title, message,
            "Order", evt.OrderId, cancellationToken);
    }

    public async Task NotifyProcessStartedAsync(ProcessStartedEvent evt, CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            _hubContext.Clients.Group($"tenant-{evt.TenantId}")
                .SendAsync("ProcessStarted", evt, cancellationToken),
            _hubContext.Clients.Group($"process-{evt.ProcessId}")
                .SendAsync("ProcessStarted", evt, cancellationToken));
    }

    public async Task NotifyProcessCompletedAsync(ProcessCompletedEvent evt, CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            _hubContext.Clients.Group($"tenant-{evt.TenantId}")
                .SendAsync("ProcessCompleted", evt, cancellationToken),
            _hubContext.Clients.Group($"process-{evt.ProcessId}")
                .SendAsync("ProcessCompleted", evt, cancellationToken));

        await CreateNotificationsForDashboardUsersAsync(evt.TenantId,
            NotificationType.ProcessCompleted,
            "Proces završen",
            $"Narudžbina #{evt.OrderNumber} — proces je završen",
            "Order", evt.OrderId, cancellationToken);
    }

    public async Task NotifyProcessBlockedAsync(ProcessBlockedEvent evt, CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            _hubContext.Clients.Group($"tenant-{evt.TenantId}")
                .SendAsync("ProcessBlocked", evt, cancellationToken),
            _hubContext.Clients.Group($"process-{evt.ProcessId}")
                .SendAsync("ProcessBlocked", evt, cancellationToken));

        await _webPushService.SendToTenantAsync(evt.TenantId,
            "Proces blokiran",
            $"Narudžbina #{evt.OrderNumber} — proces blokiran: {evt.Reason}",
            new { type = "ProcessBlocked", evt.OrderItemProcessId, evt.OrderId, evt.OrderNumber },
            cancellationToken);

        await CreateNotificationsForDashboardUsersAsync(evt.TenantId,
            NotificationType.ProcessBlocked,
            "Proces blokiran",
            $"Narudžbina #{evt.OrderNumber} — proces blokiran: {evt.Reason}",
            "Order", evt.OrderId, cancellationToken);
    }

    public async Task NotifyProcessUnblockedAsync(ProcessUnblockedEvent evt, CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            _hubContext.Clients.Group($"tenant-{evt.TenantId}")
                .SendAsync("ProcessUnblocked", evt, cancellationToken),
            _hubContext.Clients.Group($"process-{evt.ProcessId}")
                .SendAsync("ProcessUnblocked", evt, cancellationToken));
    }

    public async Task NotifyBlockRequestCreatedAsync(BlockRequestCreatedEvent evt, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"tenant-{evt.TenantId}")
            .SendAsync("BlockRequestCreated", evt, cancellationToken);

        await CreateNotificationsForDashboardUsersAsync(evt.TenantId,
            NotificationType.BlockRequest,
            "Novi zahtev za blokadu",
            "Novi zahtev za blokadu je poslat na pregled",
            "BlockRequest", evt.BlockRequestId, cancellationToken);
    }

    public async Task NotifyBlockRequestApprovedAsync(BlockRequestApprovedEvent evt, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"tenant-{evt.TenantId}")
            .SendAsync("BlockRequestApproved", evt, cancellationToken);

        var title = "Zahtev odobren";
        var message = "Zahtev za blokadu je odobren";

        await _webPushService.SendToTenantAsync(evt.TenantId,
            title, message,
            new { type = "BlockRequestApproved", evt.BlockRequestId },
            cancellationToken);

        // Notify dashboard users
        await CreateNotificationsForDashboardUsersAsync(evt.TenantId,
            NotificationType.BlockRequestApproved,
            title, message,
            "BlockRequest", evt.BlockRequestId, cancellationToken);

        // Also notify the requesting worker
        var workerNotification = Notification.Create(evt.TenantId, evt.RequestedByUserId,
            NotificationType.BlockRequestApproved, title, message,
            "BlockRequest", evt.BlockRequestId);
        await _notificationRepository.AddAsync(workerNotification, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notificationBroadcaster.BroadcastNotificationCreatedAsync(evt.TenantId, cancellationToken);
    }

    public async Task NotifyBlockRequestRejectedAsync(BlockRequestRejectedEvent evt, CancellationToken cancellationToken = default)
    {
        var title = "Zahtev odbijen";
        var message = string.IsNullOrWhiteSpace(evt.RejectionNote)
            ? "Vaš zahtev za blokadu je odbijen"
            : $"Vaš zahtev za blokadu je odbijen: {evt.RejectionNote}";

        // Send push to the requesting worker
        await _webPushService.SendToUsersAsync(
            [evt.RequestedByUserId], title, message,
            new { type = "BlockRequestRejected", evt.BlockRequestId },
            cancellationToken);

        // Create in-app notification for the requesting worker
        var notification = Notification.Create(evt.TenantId, evt.RequestedByUserId,
            NotificationType.BlockRequestRejected, title, message,
            "BlockRequest", evt.BlockRequestId);
        await _notificationRepository.AddAsync(notification, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notificationBroadcaster.BroadcastNotificationCreatedAsync(evt.TenantId, cancellationToken);
    }

    public async Task NotifyWorkerCheckedInAsync(WorkerCheckedInEvent evt, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"tenant-{evt.TenantId}")
            .SendAsync("WorkerCheckedIn", evt, cancellationToken);
    }

    public async Task NotifyWorkerCheckedOutAsync(WorkerCheckedOutEvent evt, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"tenant-{evt.TenantId}")
            .SendAsync("WorkerCheckedOut", evt, cancellationToken);
    }

    public async Task NotifyWorkerAutoLoggedOutAsync(WorkerAutoLoggedOutEvent evt, CancellationToken cancellationToken = default)
    {
        // Bojan 30.05.2026: coordinator should be warned when auto-logout fires.
        // Broadcast to tenant for live dashboards; persist a per-user Notification
        // for each dashboard role so it shows up in the notification list.
        await _hubContext.Clients.Group($"tenant-{evt.TenantId}")
            .SendAsync("WorkerAutoLoggedOut", evt, cancellationToken);

        var worker = await _userRepository.GetByIdAsync(evt.UserId, cancellationToken);
        var workerName = worker != null ? $"{worker.FirstName} {worker.LastName}" : "—";

        var title = "Auto-odjava";
        var message = $"Radnik {workerName} automatski je odjavljen.";

        var allUsers = await _userRepository.GetByTenantIdAsync(evt.TenantId, cancellationToken);
        var dashboardUserIds = allUsers
            .Where(u => u.IsActive && DashboardRoles.Contains(u.Role))
            .Select(u => u.Id)
            .ToList();

        foreach (var userId in dashboardUserIds)
        {
            var notification = Notification.Create(evt.TenantId, userId,
                NotificationType.WorkerAutoLoggedOut, title, message,
                "WorkSession", evt.SessionId);
            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notificationBroadcaster.BroadcastNotificationCreatedAsync(evt.TenantId, cancellationToken);
    }

    public async Task NotifyDeadlineWarningAsync(DeadlineWarningEvent evt, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"tenant-{evt.TenantId}")
            .SendAsync("DeadlineWarning", evt, cancellationToken);

        var levelSr = evt.Level == "Critical" ? "Kritično" : "Upozorenje";
        var title = $"Rok — {levelSr}";
        var message = $"Narudžbina #{evt.OrderNumber} — još {evt.DaysRemaining} dana";
        var pushData = new { type = "DeadlineWarning", evt.OrderId, evt.OrderNumber, evt.DaysRemaining, evt.Level };

        // Collect target user IDs: dashboard users + workers on relevant processes
        var allUsers = await _userRepository.GetByTenantIdAsync(evt.TenantId, cancellationToken);
        var dashboardUserIds = allUsers
            .Where(u => u.IsActive && DashboardRoles.Contains(u.Role))
            .Select(u => u.Id)
            .ToList();

        var workerIds = new List<Guid>();
        foreach (var processId in evt.ProcessIds)
        {
            var workers = await _userRepository.GetByProcessIdAsync(processId, evt.TenantId, cancellationToken);
            workerIds.AddRange(workers.Select(w => w.Id));
        }

        var targetUserIds = dashboardUserIds.Union(workerIds).Distinct().ToList();

        // Send targeted push
        if (targetUserIds.Count > 0)
        {
            await _webPushService.SendToUsersAsync(targetUserIds, title, message, pushData, cancellationToken);
        }

        // Create per-user in-app notifications
        var notificationType = evt.Level == "Critical"
            ? NotificationType.DeadlineCritical
            : NotificationType.DeadlineWarning;

        foreach (var userId in targetUserIds)
        {
            var notification = Notification.Create(evt.TenantId, userId, notificationType, title, message, "Order", evt.OrderId);
            await _notificationRepository.AddAsync(notification, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (targetUserIds.Count > 0)
        {
            await _notificationBroadcaster.BroadcastNotificationCreatedAsync(evt.TenantId, cancellationToken);
        }
    }

    public async Task NotifyOrderUpdatedAsync(Guid tenantId, Guid orderId, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"tenant-{tenantId}")
            .SendAsync("OrderUpdated", new { TenantId = tenantId, OrderId = orderId }, cancellationToken);
    }

    public async Task NotifyProcessReadyForQueueAsync(ProcessReadyForQueueEvent evt, CancellationToken cancellationToken = default)
    {
        // Send SignalR to workers on the target process
        await _hubContext.Clients.Group($"process-{evt.ProcessId}")
            .SendAsync("ProcessReadyForQueue", evt, cancellationToken);

        // Send push notification + in-app notification to workers assigned to this process
        var workers = await _userRepository.GetByProcessIdAsync(evt.ProcessId, evt.TenantId, cancellationToken);
        if (workers.Count > 0)
        {
            var title = "Nova narudžbina u redu";
            var message = $"Narudžbina #{evt.OrderNumber} je spremna za vaš proces";
            var workerIds = workers.Select(u => u.Id).ToList();

            await _webPushService.SendToUsersAsync(workerIds, title, message,
                new { type = "ProcessReadyForQueue", evt.OrderItemProcessId, evt.OrderId, evt.OrderNumber },
                cancellationToken);

            foreach (var workerId in workerIds)
            {
                var notification = Notification.Create(evt.TenantId, workerId,
                    NotificationType.OrderActivated, title, message, "Order", evt.OrderId);
                await _notificationRepository.AddAsync(notification, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _notificationBroadcaster.BroadcastNotificationCreatedAsync(evt.TenantId, cancellationToken);
        }
    }

    private async Task CreateNotificationsForAllUsersAsync(
        Guid tenantId,
        NotificationType type,
        string title,
        string message,
        string? referenceType,
        Guid? referenceId,
        CancellationToken cancellationToken)
    {
        var allUsers = await _userRepository.GetByTenantIdAsync(tenantId, cancellationToken);

        foreach (var user in allUsers.Where(u => u.IsActive))
        {
            var notification = Notification.Create(tenantId, user.Id, type, title, message, referenceType, referenceId);
            await _notificationRepository.AddAsync(notification, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notificationBroadcaster.BroadcastNotificationCreatedAsync(tenantId, cancellationToken);
    }

    private async Task CreateNotificationsForDashboardUsersAsync(
        Guid tenantId,
        NotificationType type,
        string title,
        string message,
        string? referenceType,
        Guid? referenceId,
        CancellationToken cancellationToken)
    {
        var allUsers = await _userRepository.GetByTenantIdAsync(tenantId, cancellationToken);
        var dashboardUsers = allUsers.Where(u => u.IsActive && DashboardRoles.Contains(u.Role));

        foreach (var user in dashboardUsers)
        {
            var notification = Notification.Create(tenantId, user.Id, type, title, message, referenceType, referenceId);
            await _notificationRepository.AddAsync(notification, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notificationBroadcaster.BroadcastNotificationCreatedAsync(tenantId, cancellationToken);
    }
}
