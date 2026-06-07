using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.PauseWork;

public class PauseWorkCommandHandler : IRequestHandler<PauseWorkCommand, Unit>
{
    private readonly IOrderItemSubProcessRepository _subProcessRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;

    public PauseWorkCommandHandler(
        IOrderItemSubProcessRepository subProcessRepository,
        IOrdersUnitOfWork unitOfWork)
    {
        _subProcessRepository = subProcessRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(PauseWorkCommand request, CancellationToken cancellationToken)
    {
        var activeLogs = await _subProcessRepository.GetActiveLogsByUserIdAsync(request.UserId, cancellationToken);

        foreach (var log in activeLogs)
        {
            log.End();
            if (log.DurationMinutes.HasValue)
                log.OrderItemSubProcess.AddDuration(log.DurationMinutes.Value);
            // Stamp PausedOnLogoutAt so ResumeOnLoginCommandHandler can
            // auto-resume on next login. Without this the sub-process logs
            // get closed here before PauseOnLogout runs, so PauseOnLogout
            // finds no open log to act on and never marks the sub-process —
            // and ResumeOnLogin has nothing to resume.
            // Bojan testing 2026-05-16: sub-process processes failed to
            // auto-resume because of this ordering.
            log.OrderItemSubProcess.PauseOnLogout();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
