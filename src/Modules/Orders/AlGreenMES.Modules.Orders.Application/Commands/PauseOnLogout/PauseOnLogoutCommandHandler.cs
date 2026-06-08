using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.PauseOnLogout;

public class PauseOnLogoutCommandHandler : IRequestHandler<PauseOnLogoutCommand>
{
    private readonly IOrderItemProcessRepository _processRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;

    public PauseOnLogoutCommandHandler(
        IOrderItemProcessRepository processRepository,
        IOrdersUnitOfWork unitOfWork)
    {
        _processRepository = processRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(PauseOnLogoutCommand request, CancellationToken cancellationToken)
    {
        var activeProcesses = await _processRepository.GetInProgressByProcessIdAsync(
            request.ProcessId, request.TenantId, cancellationToken);

        foreach (var process in activeProcesses)
        {
            var hasSubProcesses = process.SubProcesses.Any(sp => !sp.IsWithdrawn);
            if (hasSubProcesses)
            {
                var activeSub = process.SubProcesses
                    .FirstOrDefault(sp => sp.Status == SubProcessStatus.InProgress);
                if (activeSub != null)
                {
                    var openLog = activeSub.GetOpenLog();
                    var hadOpenLog = openLog != null;
                    if (openLog != null)
                    {
                        // Sub-process was actively running — close the log
                        // and mark for auto-resume on next worker login.
                        openLog.End();
                        if (openLog.DurationMinutes.HasValue)
                            activeSub.AddDuration(openLog.DurationMinutes.Value);
                        activeSub.PauseOnLogout();
                    }

                    // Also mark the parent OIP whenever this is an auto-
                    // logout (open log just closed here, OR PauseWorkCommand-
                    // Handler already closed it and set sub.PausedOnLogoutAt).
                    // A manually-paused sub-process has neither, so the parent
                    // marker stays null and ResumeOnLogin will skip it. Saša
                    // 08.06.2026 (Bug 2): sub-processes weren't resuming on
                    // OT relogin in production because the sub-level
                    // PausedOnLogoutAt didn't survive every code path —
                    // anchoring the marker on the parent OIP is more robust.
                    if (hadOpenLog || activeSub.PausedOnLogoutAt.HasValue)
                        process.MarkAutoPausedOnLogout();
                }
            }
            else
            {
                // PauseOnLogout is a no-op when the process is already paused
                // (manual pause), so manual pauses won't be marked for
                // auto-resume.
                process.PauseOnLogout();
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
