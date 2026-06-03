using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.Commands.PauseWork;
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
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly IProductionEventService _eventService;
    private readonly IMediator _mediator;

    public AutoCheckOutCommandHandler(
        IWorkSessionRepository workSessionRepository,
        IOrdersUnitOfWork unitOfWork,
        IProductionEventService eventService,
        IMediator mediator)
    {
        _workSessionRepository = workSessionRepository;
        _unitOfWork = unitOfWork;
        _eventService = eventService;
        _mediator = mediator;
    }

    public async Task<WorkSessionDto> Handle(AutoCheckOutCommand request, CancellationToken cancellationToken)
    {
        var session = await _workSessionRepository.GetActiveSessionAsync(request.UserId, cancellationToken);
        if (session == null)
            throw new DomainException("NOT_CHECKED_IN", "User does not have an active session.");

        // Pause + end all active sub-process logs. Delegated to PauseWork so
        // the behaviour stays in lockstep with the tablet's manual logout
        // flow (CheckOutPage → tabletApi.pause → PauseWorkCommand). Critically,
        // PauseWork also stamps PausedByStationAt on each sub-process so it
        // shows as "Pauzirano" to coordinators and can auto-resume on next
        // station login — auto-logout previously skipped that step and left
        // sub-processes looking "Rad u toku" with no active worker.
        // See [auto-logout-must-mirror-manual-logout] in memory.
        await _mediator.Send(new PauseWorkCommand(request.UserId), cancellationToken);

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
