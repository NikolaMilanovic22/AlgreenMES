using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.DTOs.Events;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.CheckIn;

public class CheckInCommandHandler : IRequestHandler<CheckInCommand, WorkSessionDto>
{
    private readonly IWorkSessionRepository _workSessionRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly IProductionEventService _eventService;
    private readonly IReportingQueryService _reportingQueryService;

    public CheckInCommandHandler(
        IWorkSessionRepository workSessionRepository,
        IOrdersUnitOfWork unitOfWork,
        IProductionEventService eventService,
        IReportingQueryService reportingQueryService)
    {
        _workSessionRepository = workSessionRepository;
        _unitOfWork = unitOfWork;
        _eventService = eventService;
        _reportingQueryService = reportingQueryService;
    }

    public async Task<WorkSessionDto> Handle(CheckInCommand request, CancellationToken cancellationToken)
    {
        var active = await _workSessionRepository.GetActiveSessionAsync(request.UserId, cancellationToken);
        if (active != null)
        {
            // Auto-close stale session left open from a previous day (e.g. tablet PWA closed without logout).
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (active.Date != today)
            {
                active.CheckOut();
            }
            else
            {
                throw new DomainException("ALREADY_CHECKED_IN", "User already has an active session.");
            }
        }

        // Block re-login after MaxOvertimeHours is fully consumed for today.
        // Saša 08.06.2026 (Bug 1): the worker could still log in past the cap,
        // ResumeOnLogin would briefly resume their work, then the lazy auto-
        // logout immediately closed the session — leaving the tablet UI
        // showing "logged in" while the dashboard saw no active operator.
        var quotaExhausted = await _reportingQueryService.IsOvertimeQuotaExhaustedAsync(
            request.TenantId, request.UserId, cancellationToken);
        if (quotaExhausted)
        {
            throw new DomainException(
                "OVERTIME_EXHAUSTED",
                "Maksimalno dozvoljeno prekovremeno radno vreme za danas je iskorišćeno.");
        }

        var session = WorkSession.CheckIn(request.TenantId, request.UserId);

        await _workSessionRepository.AddAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _eventService.NotifyWorkerCheckedInAsync(
            new WorkerCheckedInEvent(request.UserId, session.Id, request.TenantId), cancellationToken);

        return session.Adapt<WorkSessionDto>();
    }
}
