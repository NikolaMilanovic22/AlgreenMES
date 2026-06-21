using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.Commands.PauseOnLogout;
using AlGreenMES.Modules.Orders.Application.Commands.PauseWork;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.DTOs.Events;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using Mapster;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AlGreenMES.Modules.Orders.Application.Commands.AutoCheckOut;

public class AutoCheckOutCommandHandler : IRequestHandler<AutoCheckOutCommand, WorkSessionDto>
{
    private readonly IWorkSessionRepository _workSessionRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly IProductionEventService _eventService;
    private readonly IMediator _mediator;
    private readonly IUserProcessLookup _userProcessLookup;
    private readonly ILogger<AutoCheckOutCommandHandler> _logger;

    public AutoCheckOutCommandHandler(
        IWorkSessionRepository workSessionRepository,
        IOrdersUnitOfWork unitOfWork,
        IProductionEventService eventService,
        IMediator mediator,
        IUserProcessLookup userProcessLookup,
        ILogger<AutoCheckOutCommandHandler> logger)
    {
        _workSessionRepository = workSessionRepository;
        _unitOfWork = unitOfWork;
        _eventService = eventService;
        _mediator = mediator;
        _userProcessLookup = userProcessLookup;
        _logger = logger;
    }

    public async Task<WorkSessionDto> Handle(AutoCheckOutCommand request, CancellationToken cancellationToken)
    {
        var session = await _workSessionRepository.GetActiveSessionAsync(request.UserId, cancellationToken);
        if (session == null)
            throw new DomainException("NOT_CHECKED_IN", "User does not have an active session.");

        // Pause + end all active sub-process logs. Delegated to PauseWork so
        // the behaviour stays in lockstep with the tablet's manual logout
        // flow (CheckOutPage → tabletApi.pause → PauseWorkCommand). Critically,
        // PauseWork also stamps PausedOnLogoutAt on each sub-process so it
        // shows as "Pauzirano" to coordinators and can auto-resume on next
        // worker login — auto-logout previously skipped that step and left
        // sub-processes looking "Rad u toku" with no active worker.
        // See [auto-logout-must-mirror-manual-logout] in memory.
        await _mediator.Send(new PauseWorkCommand(request.UserId), cancellationToken);

        // Mirror the FE manual-logout chain's per-process PauseOnLogout step
        // (CheckOutPage → processWorkflowApi.pauseOnLogout per user.processes).
        // This is what handles processes that have NO sub-processes — e.g.
        // Krojenje on ORD-2026-015 — where PauseWork's subprocess-log walk
        // finds nothing to pause. Without this, those processes keep ticking
        // visually after auto-logout (Bojan 04.06.2026 test, slika 1).
        var processIds = await _userProcessLookup.GetUserProcessIdsAsync(request.UserId, cancellationToken);
        foreach (var processId in processIds)
        {
            try
            {
                await _mediator.Send(new PauseOnLogoutCommand(processId, session.TenantIdRequired, request.UserId), cancellationToken);
            }
            catch (Exception ex)
            {
                // Don't let a single per-process failure abort the whole
                // auto-checkout — the session close still needs to happen.
                _logger.LogWarning(ex,
                    "PauseOnLogout failed during auto-checkout for user {UserId} process {ProcessId}",
                    request.UserId, processId);
            }
        }

        session.AutoCheckOut(request.When);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Fire the standard CheckedOut event (for live tenant dashboards) AND
        // the coordinator-targeted AutoLoggedOut event (broadcasts + persisted
        // Notification per coordinator/manager — Bojan 30.05.2026 ask).
        await _eventService.NotifyWorkerCheckedOutAsync(
            new WorkerCheckedOutEvent(session.UserId, session.Id, session.DurationMinutes, session.TenantIdRequired), cancellationToken);
        await _eventService.NotifyWorkerAutoLoggedOutAsync(
            new WorkerAutoLoggedOutEvent(session.UserId, session.Id, session.CheckOutTime!.Value, session.DurationMinutes, session.TenantIdRequired),
            cancellationToken);

        return session.Adapt<WorkSessionDto>();
    }
}
