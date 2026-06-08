using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.ResumeOnLogin;

public class ResumeOnLoginCommandHandler : IRequestHandler<ResumeOnLoginCommand>
{
    private readonly IOrderItemProcessRepository _processRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;

    public ResumeOnLoginCommandHandler(
        IOrderItemProcessRepository processRepository,
        IOrdersUnitOfWork unitOfWork)
    {
        _processRepository = processRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(ResumeOnLoginCommand request, CancellationToken cancellationToken)
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
                // Only auto-resume sub-processes that were paused by a tablet
                // logout AND where THE SAME worker was the last one working
                // on it. Bug 07.06.2026 (Milos): a different qualified worker
                // logging in was auto-resuming sub-processes paused by other
                // workers, inflating Aktivno na procesima and silently
                // re-attributing the work.
                //
                // Check EITHER marker (sub or parent OIP). Saša 08.06.2026
                // (Bug 2): in production, the sub-level PausedOnLogoutAt
                // sometimes didn't survive every auto-logout code path, so
                // sub-processes stayed paused after OT relogin. The parent
                // OIP marker (PauseOnLogoutCommandHandler 08.06.2026) is a
                // more reliable anchor — check either to cover both cases.
                var wasAutoLoggedOut = activeSub?.PausedOnLogoutAt.HasValue == true
                                       || process.PausedOnLogoutAt.HasValue;
                if (activeSub != null
                    && wasAutoLoggedOut
                    && WasLastWorkedBy(activeSub, request.UserId))
                {
                    var openLog = activeSub.GetOpenLog();
                    if (openLog == null)
                        activeSub.StartLog(request.UserId); // also clears sub.PausedOnLogoutAt
                    process.ClearAutoLogoutMarker();
                }
            }
            else
            {
                // Only auto-resume processes paused by tablet logout —
                // manually paused processes stay paused. Same per-worker
                // scoping as the sub-process branch above.
                if (process.PausedOnLogoutAt.HasValue
                    && process.PausedAt.HasValue
                    && WasLastWorkedBy(process, request.UserId))
                    process.ResumeTimer(request.UserId); // also clears PausedOnLogoutAt
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Did the requesting worker have the most-recent log on this sub-process?
    /// Returns true also if there are no logs at all yet (then we can't say
    /// "someone else" — auto-resume keeps original behaviour).
    /// </summary>
    private static bool WasLastWorkedBy(Domain.Entities.OrderItemSubProcess sub, Guid userId)
    {
        var lastUserId = sub.Logs
            .OrderByDescending(l => l.StartTime)
            .Select(l => (Guid?)l.UserId)
            .FirstOrDefault();
        return !lastUserId.HasValue || lastUserId.Value == userId;
    }

    private static bool WasLastWorkedBy(Domain.Entities.OrderItemProcess process, Guid userId)
    {
        // For process-level work (no sub-processes) we look at OrderItemProcessLog.
        // Fall back to StartedByUserId if no logs exist (historical row pre the
        // 06.06 log table).
        var lastUserId = process.ProcessLogs
            .OrderByDescending(l => l.StartTime)
            .Select(l => (Guid?)l.UserId)
            .FirstOrDefault();
        return lastUserId.HasValue
            ? lastUserId.Value == userId
            : (!process.StartedByUserId.HasValue || process.StartedByUserId.Value == userId);
    }
}
