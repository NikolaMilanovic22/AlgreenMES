using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.DTOs.Events;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.CheckOut;

public class CheckOutCommandHandler : IRequestHandler<CheckOutCommand, WorkSessionDto>
{
    private readonly IWorkSessionRepository _workSessionRepository;
    private readonly IOrderItemSubProcessRepository _subProcessRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly IProductionEventService _eventService;

    public CheckOutCommandHandler(
        IWorkSessionRepository workSessionRepository,
        IOrderItemSubProcessRepository subProcessRepository,
        IOrdersUnitOfWork unitOfWork,
        IProductionEventService eventService)
    {
        _workSessionRepository = workSessionRepository;
        _subProcessRepository = subProcessRepository;
        _unitOfWork = unitOfWork;
        _eventService = eventService;
    }

    public async Task<WorkSessionDto> Handle(CheckOutCommand request, CancellationToken cancellationToken)
    {
        var session = await _workSessionRepository.GetActiveSessionAsync(request.UserId, cancellationToken);
        if (session == null)
            throw new DomainException("NOT_CHECKED_IN", "User does not have an active session.");

        // Close all active logs for this user
        var activeLogs = await _subProcessRepository.GetActiveLogsByUserIdAsync(request.UserId, cancellationToken);
        foreach (var log in activeLogs)
        {
            log.End();
            if (log.DurationMinutes.HasValue)
                log.OrderItemSubProcess.AddDuration(log.DurationMinutes.Value);
        }

        session.CheckOut();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _eventService.NotifyWorkerCheckedOutAsync(
            new WorkerCheckedOutEvent(session.UserId, session.Id, session.DurationMinutes, session.TenantIdRequired), cancellationToken);

        return session.Adapt<WorkSessionDto>();
    }
}
