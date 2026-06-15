using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.ResumeProcessWork;

public class ResumeProcessWorkCommandHandler : IRequestHandler<ResumeProcessWorkCommand, Unit>
{
    private readonly IOrderItemProcessRepository _processRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;

    public ResumeProcessWorkCommandHandler(
        IOrderItemProcessRepository processRepository,
        IOrdersUnitOfWork unitOfWork)
    {
        _processRepository = processRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(ResumeProcessWorkCommand request, CancellationToken cancellationToken)
    {
        var process = await _processRepository.GetByIdWithFullDetailsAsync(request.OrderItemProcessId, cancellationToken);
        if (process == null)
            throw new NotFoundException("OrderItemProcess", request.OrderItemProcessId);

        if (process.OrderItem.Order.Status != OrderStatus.Active)
            throw new DomainException("ORDER_NOT_ACTIVE", "Order must be active.");

        if (process.Status != ProcessStatus.InProgress)
            throw new DomainException("INVALID_STATUS", "Process must be in progress to resume.");

        var hasSubProcesses = process.SubProcesses.Any(sp => !sp.IsWithdrawn);

        if (!hasSubProcesses)
        {
            process.ResumeTimer(request.UserId);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Unit.Value;
        }

        var activeSubProcess = process.SubProcesses
            .FirstOrDefault(sp => sp.Status == SubProcessStatus.InProgress);

        if (activeSubProcess == null)
            throw new DomainException("NO_ACTIVE_SUBPROCESS", "No in-progress sub-process found to resume.");

        var existingOpenLog = activeSubProcess.GetOpenLog();
        if (existingOpenLog != null)
            throw new DomainException("ALREADY_RUNNING", "Work is already in progress. Stop before resuming.");

        activeSubProcess.StartLog(request.UserId);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
