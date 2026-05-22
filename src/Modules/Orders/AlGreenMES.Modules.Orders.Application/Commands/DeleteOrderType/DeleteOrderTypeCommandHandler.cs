using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.DeleteOrderType;

public class DeleteOrderTypeCommandHandler : IRequestHandler<DeleteOrderTypeCommand, DeleteOrderTypeResult>
{
    private readonly IOrderTypeRepository _repository;
    private readonly IOrdersUnitOfWork _unitOfWork;

    public DeleteOrderTypeCommandHandler(IOrderTypeRepository repository, IOrdersUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<DeleteOrderTypeResult> Handle(DeleteOrderTypeCommand request, CancellationToken cancellationToken)
    {
        var orderType = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("OrderType", request.Id);

        var inUse = await _repository.IsInUseAsync(request.Id, cancellationToken);
        if (inUse)
        {
            // In use → soft delete (deactivate). Existing orders still reference it.
            orderType.Deactivate();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return new DeleteOrderTypeResult(HardDeleted: false, Deactivated: true);
        }

        _repository.Remove(orderType);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new DeleteOrderTypeResult(HardDeleted: true, Deactivated: false);
    }
}
