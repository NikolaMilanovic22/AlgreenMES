using AlGreenMES.BuildingBlocks.Common.Entities;
using AlGreenMES.BuildingBlocks.Common.Exceptions;

namespace AlGreenMES.Modules.Orders.Domain.Entities;

public class OrderItemSubProcessLog : TenantEntity
{
    public Guid OrderItemSubProcessId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime StartTime { get; private set; }
    public DateTime? EndTime { get; private set; }
    public int? DurationMinutes { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public OrderItemSubProcess OrderItemSubProcess { get; private set; } = null!;

    private OrderItemSubProcessLog()
    {
    }

    // occurredAt lets a caller stamp the real moment work started — used when
    // a tablet action was taken offline and replayed later. Null = now.
    public static OrderItemSubProcessLog Start(Guid tenantId, Guid orderItemSubProcessId, Guid userId, DateTime? occurredAt = null)
    {
        return new OrderItemSubProcessLog
        {
            TenantId = tenantId,
            OrderItemSubProcessId = orderItemSubProcessId,
            UserId = userId,
            StartTime = occurredAt ?? DateTime.UtcNow
        };
    }

    public void End(DateTime? occurredAt = null)
    {
        if (EndTime.HasValue)
            throw new DomainException("ALREADY_ENDED", "Log already ended.");
        EndTime = occurredAt ?? DateTime.UtcNow;
        DurationMinutes = (int)(EndTime.Value - StartTime).TotalSeconds;
    }
}
