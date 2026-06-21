using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.DTOs.Events;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.ApproveChangeRequest;

public class ApproveChangeRequestCommandHandler : IRequestHandler<ApproveChangeRequestCommand, ChangeRequestDto>
{
    private readonly IChangeRequestRepository _changeRequestRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly IProductionEventService _productionEventService;

    public ApproveChangeRequestCommandHandler(
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

    public async Task<ChangeRequestDto> Handle(ApproveChangeRequestCommand request, CancellationToken cancellationToken)
    {
        var changeRequest = await _changeRequestRepository.GetByIdAsync(request.Id, cancellationToken);
        if (changeRequest == null)
            throw new NotFoundException("ChangeRequest", request.Id);

        changeRequest.Approve(request.HandledByUserId, request.ResponseNote);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var order = await _orderRepository.GetByIdAsync(changeRequest.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order", changeRequest.OrderId);
        await _productionEventService.NotifyChangeRequestApprovedAsync(
            new ChangeRequestApprovedEvent(
                changeRequest.Id,
                changeRequest.OrderId,
                order.OrderNumber,
                changeRequest.RequestedByUserId,
                changeRequest.TenantIdRequired),
            cancellationToken);

        return changeRequest.Adapt<ChangeRequestDto>();
    }
}
