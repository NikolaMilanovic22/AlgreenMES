using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.DTOs.Events;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.AutoCheckOut;

public class AutoCheckOutCommandHandler : IRequestHandler<AutoCheckOutCommand, WorkSessionDto>
{
    private readonly IWorkSessionRepository _workSessionRepository;
    private readonly IOrderItemSubProcessRepository _subProcessRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly IProductionEventService _eventService;

    public AutoCheckOutCommandHandler(
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

    public async Task<WorkSessionDto> Handle(AutoCheckOutCommand request, CancellationToken cancellationToken)
    {
        var session = await _workSessionRepository.GetActiveSessionAsync(request.UserId, cancellationToken);
        if (session == null)
            throw new DomainException("NOT_CHECKED_IN", "User does not have an active session.");

        // Close any active sub-process logs (same as manual checkout — a worker
        // who hit the cap might still have a process timer running).
        var activeLogs = await _subProcessRepository.GetActiveLogsByUserIdAsync(request.UserId, cancellationToken);
        foreach (var log in activeLogs)
        {
            log.End();
            if (log.DurationMinutes.HasValue)
                log.OrderItemSubProcess.AddDuration(log.DurationMinutes.Value);
        }

        session.AutoCheckOut(request.When);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Fire the standard CheckedOut event (for live tenant dashboards) AND
        // the coordinator-targeted AutoLoggedOut event (broadcasts + persisted
        // Notification per coordinator/manager — Bojan 30.05.2026 ask).
        await _eventService.NotifyWorkerCheckedOutAsync(
            new WorkerCheckedOutEvent(session.UserId, session.Id, session.DurationMinutes, session.TenantId), cancellationToken);
        await _eventService.NotifyWorkerAutoLoggedOutAsync(
            new WorkerAutoLoggedOutEvent(session.UserId, session.Id, session.CheckOutTime!.Value, session.DurationMinutes, session.TenantId),
            cancellationToken);

        return session.Adapt<WorkSessionDto>();
    }
}
