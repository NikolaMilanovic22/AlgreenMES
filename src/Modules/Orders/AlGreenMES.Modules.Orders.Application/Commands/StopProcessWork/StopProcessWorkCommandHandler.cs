using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.StopProcessWork;

public class StopProcessWorkCommandHandler : IRequestHandler<StopProcessWorkCommand, Unit>
{
    private readonly IOrderItemProcessRepository _processRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly IProcessedActionStore _idempotency;

    public StopProcessWorkCommandHandler(
        IOrderItemProcessRepository processRepository,
        IOrdersUnitOfWork unitOfWork,
        IProcessedActionStore idempotency)
    {
        _processRepository = processRepository;
        _unitOfWork = unitOfWork;
        _idempotency = idempotency;
    }

    public async Task<Unit> Handle(StopProcessWorkCommand request, CancellationToken cancellationToken)
    {
        var process = await _processRepository.GetByIdWithFullDetailsAsync(request.OrderItemProcessId, cancellationToken);
        if (process == null)
            throw new NotFoundException("OrderItemProcess", request.OrderItemProcessId);

        // Idempotency: a replayed stop is a no-op (the time was already booked).
        if (request.ActionId.HasValue && await _idempotency.ExistsAsync(request.ActionId.Value, cancellationToken))
            return Unit.Value;

        if (process.OrderItem.Order.Status != OrderStatus.Active)
            throw new DomainException("ORDER_NOT_ACTIVE", "Order must be active.");

        if (process.Status != ProcessStatus.InProgress)
            throw new DomainException("INVALID_STATUS", "Process must be in progress to stop work.");

        var hasSubProcesses = process.SubProcesses.Any(sp => !sp.IsWithdrawn);

        if (hasSubProcesses)
        {
            var activeSubProcess = process.SubProcesses
                .FirstOrDefault(sp => sp.Status == SubProcessStatus.InProgress);

            if (activeSubProcess != null)
            {
                var openLog = activeSubProcess.GetOpenLog();
                if (openLog != null)
                {
                    openLog.End(request.OccurredAt);
                    if (openLog.DurationMinutes.HasValue)
                        activeSubProcess.AddDuration(openLog.DurationMinutes.Value);
                }
            }
        }
        else
        {
            process.Pause(request.OccurredAt);
        }

        if (request.ActionId.HasValue)
            _idempotency.Record(process.TenantIdRequired, request.ActionId.Value, "StopProcessWork");

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
