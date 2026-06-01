using AlGreenMES.Modules.Orders.Application.DTOs.Events;

namespace AlGreenMES.Modules.Orders.Application.Interfaces;

public interface IProductionEventService
{
    Task NotifyOrderActivatedAsync(OrderActivatedEvent evt, CancellationToken cancellationToken = default);
    Task NotifyProcessStartedAsync(ProcessStartedEvent evt, CancellationToken cancellationToken = default);
    Task NotifyProcessCompletedAsync(ProcessCompletedEvent evt, CancellationToken cancellationToken = default);
    Task NotifyProcessBlockedAsync(ProcessBlockedEvent evt, CancellationToken cancellationToken = default);
    Task NotifyProcessUnblockedAsync(ProcessUnblockedEvent evt, CancellationToken cancellationToken = default);
    Task NotifyBlockRequestCreatedAsync(BlockRequestCreatedEvent evt, CancellationToken cancellationToken = default);
    Task NotifyBlockRequestApprovedAsync(BlockRequestApprovedEvent evt, CancellationToken cancellationToken = default);
    Task NotifyBlockRequestRejectedAsync(BlockRequestRejectedEvent evt, CancellationToken cancellationToken = default);
    Task NotifyWorkerCheckedInAsync(WorkerCheckedInEvent evt, CancellationToken cancellationToken = default);
    Task NotifyWorkerCheckedOutAsync(WorkerCheckedOutEvent evt, CancellationToken cancellationToken = default);
    Task NotifyWorkerAutoLoggedOutAsync(WorkerAutoLoggedOutEvent evt, CancellationToken cancellationToken = default);
    Task NotifyDeadlineWarningAsync(DeadlineWarningEvent evt, CancellationToken cancellationToken = default);
    Task NotifyProcessReadyForQueueAsync(ProcessReadyForQueueEvent evt, CancellationToken cancellationToken = default);
    Task NotifyOrderUpdatedAsync(Guid tenantId, Guid orderId, CancellationToken cancellationToken = default);
}
