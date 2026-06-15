using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.DTOs.Events;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.CreateChangeRequest;

public class CreateChangeRequestCommandHandler : IRequestHandler<CreateChangeRequestCommand, ChangeRequestDto>
{
    private readonly IChangeRequestRepository _changeRequestRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly IProductionEventService _productionEventService;

    public CreateChangeRequestCommandHandler(
        IChangeRequestRepository changeRequestRepository,
        IOrderRepository orderRepository,
        IOrdersUnitOfWork unitOfWork,
        IProductionEventService productionEventService)
    {
        _changeRequestRepository = changeRequestRepository;
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _productionEventService = productionEventService;
    }

    public async Task<ChangeRequestDto> Handle(CreateChangeRequestCommand request, CancellationToken cancellationToken)
    {
        var changeRequest = ChangeRequest.Create(
            request.TenantId,
            request.OrderId,
            request.RequestedByUserId,
            request.RequestType,
            request.Description);

        await _changeRequestRepository.AddAsync(changeRequest, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order", request.OrderId);
        await _productionEventService.NotifyChangeRequestCreatedAsync(
            new ChangeRequestCreatedEvent(
                changeRequest.Id,
                request.OrderId,
                order.OrderNumber,
                request.RequestType.ToString(),
                request.TenantId),
            cancellationToken);

        return changeRequest.Adapt<ChangeRequestDto>();
    }
}
