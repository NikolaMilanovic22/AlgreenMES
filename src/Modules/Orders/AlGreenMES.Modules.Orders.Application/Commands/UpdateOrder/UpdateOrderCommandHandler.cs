using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Production.Domain.Enums;
using AlGreenMES.Modules.Production.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.UpdateOrder;

public class UpdateOrderCommandHandler : IRequestHandler<UpdateOrderCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly IProcessRepository _processRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;

    private readonly IProductionEventService _eventService;

    public UpdateOrderCommandHandler(
        IOrderRepository orderRepository,
        IProductCategoryRepository categoryRepository,
        IProcessRepository processRepository,
        IOrdersUnitOfWork unitOfWork,
        IProductionEventService eventService)
    {
        _orderRepository = orderRepository;
        _categoryRepository = categoryRepository;
        _processRepository = processRepository;
        _unitOfWork = unitOfWork;
        _eventService = eventService;
    }

    public async Task<OrderDto> Handle(UpdateOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdWithFullDetailsAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Order", request.Id);

        order.Update(request.OrderNumber, request.DeliveryDate, request.Notes, request.CustomWarningDays, request.CustomCriticalDays);

        // Remove items
        if (request.RemoveItemIds is { Count: > 0 })
        {
            foreach (var itemId in request.RemoveItemIds)
            {
                order.RemoveItem(itemId);
            }
        }

        // Add items
        if (request.AddItems is { Count: > 0 })
        {
            foreach (var itemInput in request.AddItems)
            {
                var category = await _categoryRepository.GetByIdWithDetailsAsync(itemInput.ProductCategoryId, cancellationToken)
                    ?? throw new NotFoundException("ProductCategory", itemInput.ProductCategoryId);

                var item = order.AddItem(itemInput.ProductCategoryId, itemInput.ProductName, itemInput.Quantity, itemInput.Notes);

                // Mirror CreateOrder/AddOrderItem: when the order has manual
                // processes, those override the category list for new items.
                // Without this, adding an item via the drawer's "Dodaj stavku"
                // (which posts UpdateOrder with AddItems) silently bypassed the
                // manual list and the new item got the full category processes.
                IEnumerable<(Guid ProcessId, ComplexityType? Complexity)> processSource = order.HasManualProcesses
                    ? order.ManualProcesses
                        .OrderBy(p => p.SequenceOrder)
                        .Select(p => (p.ProcessId, p.DefaultComplexity))
                    : category.Processes
                        .OrderBy(p => p.SequenceOrder)
                        .Select(p => (p.ProcessId, p.DefaultComplexity));

                foreach (var (processId, complexity) in processSource)
                {
                    var oip = item.AddProcess(processId, complexity);

                    var process = await _processRepository.GetByIdWithSubProcessesAsync(processId, cancellationToken);
                    if (process?.SubProcesses != null)
                    {
                        foreach (var sub in process.SubProcesses.Where(s => s.IsActive).OrderBy(s => s.SequenceOrder))
                        {
                            oip.AddSubProcess(sub.Id);
                        }
                    }
                }

                _orderRepository.AddItem(item);
            }
        }

        // Complexity overrides
        if (request.ComplexityOverrides is { Count: > 0 })
        {
            if (order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.Completed)
                throw new DomainException("INVALID_STATUS", "Cannot override complexity on completed or cancelled orders.");

            foreach (var co in request.ComplexityOverrides)
            {
                var item = order.Items.FirstOrDefault(i => i.Id == co.ItemId)
                    ?? throw new NotFoundException("OrderItem", co.ItemId);
                var proc = item.Processes.FirstOrDefault(p => p.Id == co.ProcessId)
                    ?? throw new NotFoundException("OrderItemProcess", co.ProcessId);
                proc.OverrideComplexity(co.Complexity);
            }
        }

        // Add special requests
        if (request.AddSpecialRequests is { Count: > 0 })
        {
            if (order.Status != OrderStatus.Draft)
                throw new DomainException("ORDER_NOT_DRAFT", "Can only add special requests to draft orders.");

            foreach (var sr in request.AddSpecialRequests)
            {
                var item = order.Items.FirstOrDefault(i => i.Id == sr.ItemId)
                    ?? throw new NotFoundException("OrderItem", sr.ItemId);
                item.AddSpecialRequest(sr.SpecialRequestTypeId);
            }
        }

        // Remove special requests
        if (request.RemoveSpecialRequests is { Count: > 0 })
        {
            if (order.Status != OrderStatus.Draft)
                throw new DomainException("ORDER_NOT_DRAFT", "Can only remove special requests from draft orders.");

            foreach (var sr in request.RemoveSpecialRequests)
            {
                var item = order.Items.FirstOrDefault(i => i.Id == sr.ItemId)
                    ?? throw new NotFoundException("OrderItem", sr.ItemId);
                var specialRequest = item.SpecialRequests.FirstOrDefault(s => s.Id == sr.SpecialRequestId)
                    ?? throw new NotFoundException("SpecialRequest", sr.SpecialRequestId);
                item.RemoveSpecialRequest(specialRequest.SpecialRequestTypeId);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _eventService.NotifyOrderUpdatedAsync(order.TenantId, order.Id, cancellationToken);

        return order.Adapt<OrderDto>();
    }
}
