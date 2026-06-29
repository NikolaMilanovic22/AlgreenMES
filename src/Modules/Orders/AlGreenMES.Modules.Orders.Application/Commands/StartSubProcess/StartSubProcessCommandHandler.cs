using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Production.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.StartSubProcess;

public class StartSubProcessCommandHandler : IRequestHandler<StartSubProcessCommand, OrderItemSubProcessDto>
{
    private readonly IOrderItemSubProcessRepository _subProcessRepository;
    private readonly IProcessRepository _productionProcessRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly IProcessedActionStore _idempotency;

    public StartSubProcessCommandHandler(
        IOrderItemSubProcessRepository subProcessRepository,
        IProcessRepository productionProcessRepository,
        IOrdersUnitOfWork unitOfWork,
        IProcessedActionStore idempotency)
    {
        _subProcessRepository = subProcessRepository;
        _productionProcessRepository = productionProcessRepository;
        _unitOfWork = unitOfWork;
        _idempotency = idempotency;
    }

    public async Task<OrderItemSubProcessDto> Handle(StartSubProcessCommand request, CancellationToken cancellationToken)
    {
        var subProcess = await _subProcessRepository.GetByIdWithFullDetailsAsync(request.OrderItemSubProcessId, cancellationToken);
        if (subProcess == null)
            throw new NotFoundException("OrderItemSubProcess", request.OrderItemSubProcessId);

        // Idempotency: a replayed sub-process start returns current state.
        if (request.ActionId.HasValue && await _idempotency.ExistsAsync(request.ActionId.Value, cancellationToken))
            return subProcess.Adapt<OrderItemSubProcessDto>();

        var process = subProcess.OrderItemProcess;

        if (process.OrderItem.Order.Status != OrderStatus.Active)
            throw new DomainException("ORDER_NOT_ACTIVE", "Order must be active to start a sub-process.");

        if (process.Status != ProcessStatus.InProgress)
            throw new DomainException("PROCESS_NOT_STARTED", "Parent process must be in progress.");

        // Load production process to get SubProcess SequenceOrder for correct ordering
        var productionProcess = await _productionProcessRepository.GetByIdWithSubProcessesAsync(process.ProcessId, cancellationToken);
        var subProcessOrder = productionProcess?.SubProcesses
            .ToDictionary(sp => sp.Id, sp => sp.SequenceOrder) ?? new();

        // Validate strict order: all previous sub-processes must be Completed
        var siblingSubProcesses = process.SubProcesses
            .Where(sp => !sp.IsWithdrawn)
            .OrderBy(sp => subProcessOrder.GetValueOrDefault(sp.SubProcessId, 0))
            .ToList();

        var currentIndex = siblingSubProcesses.FindIndex(sp => sp.Id == subProcess.Id);
        for (int i = 0; i < currentIndex; i++)
        {
            if (siblingSubProcesses[i].Status != SubProcessStatus.Completed)
                throw new DomainException("PREVIOUS_NOT_COMPLETED",
                    "Previous sub-process must be completed before starting this one.");
        }

        subProcess.Start();
        subProcess.StartLog(request.UserId, request.OccurredAt);

        if (request.ActionId.HasValue)
            _idempotency.Record(subProcess.TenantIdRequired, request.ActionId.Value, "StartSubProcess");

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return subProcess.Adapt<OrderItemSubProcessDto>();
    }
}
