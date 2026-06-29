using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.DTOs.Events;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Production.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AlGreenMES.Modules.Orders.Application.Commands.CompleteProcess;

public class CompleteProcessCommandHandler : IRequestHandler<CompleteProcessCommand, Unit>
{
    private readonly IOrderItemProcessRepository _orderItemProcessRepository;
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly IProductionEventService _eventService;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderAttachmentRepository _attachmentRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly IProcessedActionStore _idempotency;
    private readonly ILogger<CompleteProcessCommandHandler> _logger;

    public CompleteProcessCommandHandler(
        IOrderItemProcessRepository orderItemProcessRepository,
        IProductCategoryRepository categoryRepository,
        IOrdersUnitOfWork unitOfWork,
        IProductionEventService eventService,
        IOrderRepository orderRepository,
        IOrderAttachmentRepository attachmentRepository,
        IFileStorageService fileStorageService,
        IProcessedActionStore idempotency,
        ILogger<CompleteProcessCommandHandler> logger)
    {
        _orderItemProcessRepository = orderItemProcessRepository;
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
        _eventService = eventService;
        _orderRepository = orderRepository;
        _attachmentRepository = attachmentRepository;
        _fileStorageService = fileStorageService;
        _idempotency = idempotency;
        _logger = logger;
    }

    public async Task<Unit> Handle(CompleteProcessCommand request, CancellationToken cancellationToken)
    {
        var process = await _orderItemProcessRepository.GetByIdWithOrderDetailsAsync(request.OrderItemProcessId, cancellationToken);
        if (process == null)
            throw new NotFoundException("OrderItemProcess", request.OrderItemProcessId);

        // Idempotency: a replayed complete is a no-op — re-running would throw
        // "can only complete in-progress processes" since it is already done.
        if (request.ActionId.HasValue && await _idempotency.ExistsAsync(request.ActionId.Value, cancellationToken))
            return Unit.Value;

        process.Complete(request.OccurredAt);
        if (request.ActionId.HasValue)
            _idempotency.Record(process.TenantIdRequired, request.ActionId.Value, "CompleteProcess");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var orderId = process.OrderItem.Order.Id;
        var orderNumber = process.OrderItem.Order.OrderNumber;
        var tenantId = process.TenantIdRequired;

        await _eventService.NotifyProcessCompletedAsync(
            new ProcessCompletedEvent(process.Id, process.ProcessId, orderId, orderNumber, tenantId),
            cancellationToken);

        // Check if any downstream processes are now ready (all dependencies met)
        var siblingProcesses = await _orderItemProcessRepository.GetByOrderItemIdAsync(process.OrderItemId, cancellationToken);
        var category = await _categoryRepository.GetByIdWithDetailsAsync(process.OrderItem.ProductCategoryId, cancellationToken);
        var dependencies = category?.Dependencies ?? [];

        foreach (var sibling in siblingProcesses)
        {
            if (sibling.Id == process.Id) continue;
            if (sibling.Status != ProcessStatus.Pending) continue;
            if (sibling.IsWithdrawn) continue;

            // Get this sibling's dependencies
            var siblingDeps = dependencies
                .Where(d => d.ProcessId == sibling.ProcessId)
                .Select(d => d.DependsOnProcessId)
                .ToList();

            // Must depend on the just-completed process (otherwise this completion didn't affect it)
            if (!siblingDeps.Contains(process.ProcessId)) continue;

            // Check if ALL dependencies are now met
            var allDepsCompleted = siblingDeps.All(depProcessId =>
                siblingProcesses.Any(p =>
                    p.ProcessId == depProcessId &&
                    (p.Status == ProcessStatus.Completed || p.Status == ProcessStatus.Withdrawn)));

            if (allDepsCompleted)
            {
                await _eventService.NotifyProcessReadyForQueueAsync(
                    new ProcessReadyForQueueEvent(sibling.Id, sibling.ProcessId, orderId, orderNumber, tenantId),
                    cancellationToken);
            }
        }

        // Check if order is now fully completed
        var order = await _orderRepository.GetByIdWithFullDetailsAsync(orderId, cancellationToken);
        if (order != null && order.Items.All(i => i.IsCompleted()))
        {
            order.MarkCompleted();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
