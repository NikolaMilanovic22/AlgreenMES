using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.RestartProcess;

public class RestartProcessCommandHandler : IRequestHandler<RestartProcessCommand, Unit>
{
    private readonly IOrderItemProcessRepository _processRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly IProductionEventService _eventService;

    public RestartProcessCommandHandler(
        IOrderItemProcessRepository processRepository,
        IOrdersUnitOfWork unitOfWork,
        IProductionEventService eventService)
    {
        _processRepository = processRepository;
        _unitOfWork = unitOfWork;
        _eventService = eventService;
    }

    public async Task<Unit> Handle(RestartProcessCommand request, CancellationToken cancellationToken)
    {
        var process = await _processRepository.GetByIdWithFullDetailsAsync(request.OrderItemProcessId, cancellationToken);
        if (process == null)
            throw new NotFoundException("OrderItemProcess", request.OrderItemProcessId);

        var order = process.OrderItem.Order;
        if (order.Status != OrderStatus.Active && order.Status != OrderStatus.Completed)
            throw new DomainException("ORDER_NOT_ACTIVE", "Order must be active or completed.");

        process.Restart(request.ResetTime);

        // If order was completed, revert to active
        if (order.Status == OrderStatus.Completed)
            order.UndoComplete();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _eventService.NotifyOrderUpdatedAsync(
            process.OrderItem.Order.TenantIdRequired, process.OrderItem.Order.Id, cancellationToken);

        return Unit.Value;
    }
}
