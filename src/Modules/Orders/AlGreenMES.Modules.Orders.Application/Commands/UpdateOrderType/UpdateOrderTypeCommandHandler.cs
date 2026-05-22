using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.UpdateOrderType;

public class UpdateOrderTypeCommandHandler : IRequestHandler<UpdateOrderTypeCommand, OrderTypeDto>
{
    private readonly IOrderTypeRepository _repository;
    private readonly IOrdersUnitOfWork _unitOfWork;

    public UpdateOrderTypeCommandHandler(IOrderTypeRepository repository, IOrdersUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<OrderTypeDto> Handle(UpdateOrderTypeCommand request, CancellationToken cancellationToken)
    {
        var orderType = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("OrderType", request.Id);

        orderType.Update(request.Name, request.AllowsManualProcesses);
        if (request.IsActive) orderType.Activate(); else orderType.Deactivate();

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return orderType.Adapt<OrderTypeDto>();
    }
}
