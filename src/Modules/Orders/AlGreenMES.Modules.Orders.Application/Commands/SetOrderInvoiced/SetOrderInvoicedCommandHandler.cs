using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.SetOrderInvoiced;

public class SetOrderInvoicedCommandHandler : IRequestHandler<SetOrderInvoicedCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly IProductionEventService _eventService;

    public SetOrderInvoicedCommandHandler(IOrderRepository orderRepository, IOrdersUnitOfWork unitOfWork, IProductionEventService eventService)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _eventService = eventService;
    }

    public async Task<OrderDto> Handle(SetOrderInvoicedCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdWithItemsAsync(request.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order", request.OrderId);

        order.SetInvoiced(request.IsInvoiced);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _eventService.NotifyOrderUpdatedAsync(order.TenantIdRequired, order.Id, cancellationToken);

        return order.Adapt<OrderDto>();
    }
}
