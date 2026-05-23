using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.SetProcessExcludedFromReports;

public class SetProcessExcludedFromReportsCommandHandler
    : IRequestHandler<SetProcessExcludedFromReportsCommand, Unit>
{
    private readonly IOrderItemProcessRepository _orderItemProcessRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;

    public SetProcessExcludedFromReportsCommandHandler(
        IOrderItemProcessRepository orderItemProcessRepository,
        IOrdersUnitOfWork unitOfWork)
    {
        _orderItemProcessRepository = orderItemProcessRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(SetProcessExcludedFromReportsCommand request, CancellationToken cancellationToken)
    {
        var process = await _orderItemProcessRepository.GetByIdAsync(request.OrderItemProcessId, cancellationToken);
        if (process == null)
            throw new NotFoundException("OrderItemProcess", request.OrderItemProcessId);

        process.SetExcludedFromReports(request.Excluded);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
