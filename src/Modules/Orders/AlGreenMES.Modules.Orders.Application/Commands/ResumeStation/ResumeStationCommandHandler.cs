using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.ResumeStation;

public class ResumeStationCommandHandler : IRequestHandler<ResumeStationCommand>
{
    private readonly IOrderItemProcessRepository _processRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;

    public ResumeStationCommandHandler(
        IOrderItemProcessRepository processRepository,
        IOrdersUnitOfWork unitOfWork)
    {
        _processRepository = processRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(ResumeStationCommand request, CancellationToken cancellationToken)
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
                // Only auto-resume sub-processes that the station paused at
                // logout. Manually paused sub-processes have no PausedByStationAt
                // and stay paused.
                if (activeSub != null && activeSub.PausedByStationAt.HasValue)
                {
                    var openLog = activeSub.GetOpenLog();
                    if (openLog == null)
                        activeSub.StartLog(request.UserId); // also clears PausedByStationAt
                }
            }
            else
            {
                // Only auto-resume processes paused by station logout —
                // manually paused processes stay paused.
                if (process.PausedByStationAt.HasValue && process.PausedAt.HasValue)
                    process.ResumeTimer(); // also clears PausedByStationAt
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
