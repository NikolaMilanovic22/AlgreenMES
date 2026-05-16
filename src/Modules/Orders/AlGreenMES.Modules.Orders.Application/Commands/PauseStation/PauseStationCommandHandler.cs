using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.PauseStation;

public class PauseStationCommandHandler : IRequestHandler<PauseStationCommand>
{
    private readonly IOrderItemProcessRepository _processRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;

    public PauseStationCommandHandler(
        IOrderItemProcessRepository processRepository,
        IOrdersUnitOfWork unitOfWork)
    {
        _processRepository = processRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(PauseStationCommand request, CancellationToken cancellationToken)
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
                    if (openLog != null)
                    {
                        // Sub-process was actively running — close the log and
                        // mark for auto-resume on next station login.
                        openLog.End();
                        if (openLog.DurationMinutes.HasValue)
                            activeSub.AddDuration(openLog.DurationMinutes.Value);
                        activeSub.PauseByStation();
                    }
                    // else: sub-process is InProgress but has no open log,
                    // meaning a worker manually paused it. Leave alone.
                }
            }
            else
            {
                // PauseByStation is a no-op when the process is already paused
                // (manual pause), so manual pauses won't be marked for
                // auto-resume.
                process.PauseByStation();
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
