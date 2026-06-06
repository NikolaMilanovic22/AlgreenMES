using AlGreenMES.BuildingBlocks.Common.Entities;
using AlGreenMES.BuildingBlocks.Common.Exceptions;

namespace AlGreenMES.Modules.Orders.Domain.Entities;

/// <summary>
/// Per-work-period log of process-level work (processes WITHOUT sub-processes,
/// e.g. Krojenje). Mirrors OrderItemSubProcessLog so the reporting code can
/// treat both kinds the same way.
///
/// Created at Start / ResumeTimer; ended at Pause / PauseByStation /
/// Complete / Stop. Multiple logs per process across Start→Pause→Resume→
/// Pause→Resume… cycles, each carrying the user who was working that
/// period. Bojan 06.06.2026: needed because the previous (StartedAt →
/// PausedAt ?? CompletedAt) shortcut overcounted offline gaps (e.g.,
/// auto-logout to relogin) as continuously active.
/// </summary>
public class OrderItemProcessLog : TenantEntity
{
    public Guid OrderItemProcessId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime StartTime { get; private set; }
    public DateTime? EndTime { get; private set; }
    public int? DurationSeconds { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public OrderItemProcess OrderItemProcess { get; private set; } = null!;

    private OrderItemProcessLog()
    {
    }

    public static OrderItemProcessLog Start(Guid tenantId, Guid orderItemProcessId, Guid userId)
    {
        return new OrderItemProcessLog
        {
            TenantId = tenantId,
            OrderItemProcessId = orderItemProcessId,
            UserId = userId,
            StartTime = DateTime.UtcNow
        };
    }

    public void End()
    {
        if (EndTime.HasValue)
            throw new DomainException("ALREADY_ENDED", "Process log already ended.");
        EndTime = DateTime.UtcNow;
        DurationSeconds = (int)(EndTime.Value - StartTime).TotalSeconds;
    }
}
