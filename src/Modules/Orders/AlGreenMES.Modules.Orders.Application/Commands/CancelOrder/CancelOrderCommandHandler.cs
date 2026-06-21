using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.CancelOrder;

public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, Unit>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;

    private readonly IProductionEventService _eventService;

    public CancelOrderCommandHandler(IOrderRepository orderRepository, IOrdersUnitOfWork unitOfWork, IProductionEventService eventService)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _eventService = eventService;
    }

    public async Task<Unit> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdWithFullDetailsAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Order", request.Id);

        order.Cancel();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _eventService.NotifyOrderUpdatedAsync(order.TenantIdRequired, order.Id, cancellationToken);

        return Unit.Value;
    }
}
