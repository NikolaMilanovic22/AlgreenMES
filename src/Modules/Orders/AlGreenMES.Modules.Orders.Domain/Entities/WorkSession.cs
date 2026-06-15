using AlGreenMES.BuildingBlocks.Common.Entities;
using AlGreenMES.BuildingBlocks.Common.Exceptions;

namespace AlGreenMES.Modules.Orders.Domain.Entities;

public class WorkSession : TenantEntity
{
    public Guid UserId { get; private set; }
    public DateTime CheckInTime { get; private set; }
    public DateTime? CheckOutTime { get; private set; }
    public int? DurationMinutes { get; private set; }
    public DateOnly Date { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }
    // True when the session was closed by the auto-logout cap (Bojan 30.05.2026).
    // Used by the tablet to show the auto-logout screen, by the coordinator
    // notification, and to distinguish regular vs overtime re-login on check-in.
    public bool WasAutoClosed { get; private set; }

    public bool IsActive => !CheckOutTime.HasValue;

    private WorkSession()
    {
    }

    public static WorkSession CheckIn(Guid tenantId, Guid userId)
    {
        return new WorkSession
        {
            TenantId = tenantId,
            UserId = userId,
            CheckInTime = DateTime.UtcNow,
            Date = DateOnly.FromDateTime(DateTime.UtcNow)
        };
    }

    public void CheckOut()
    {
        if (CheckOutTime.HasValue)
            throw new DomainException("ALREADY_CHECKED_OUT", "Already checked out.");
        CheckOutTime = DateTime.UtcNow;
        DurationMinutes = (int)(CheckOutTime.Value - CheckInTime).TotalMinutes;
        UpdatedAt = DateTime.UtcNow;
    }

    // Close the session because the auto-logout cap was reached. Pass `when` =
    // the cap moment (so the recorded checkout matches when it actually
    // expired, not when the server processed it — matters for the lazy
    // server-side safety net). Defaults to UtcNow for the tablet-driven path.
    public void AutoCheckOut(DateTime? when = null)
    {
        if (CheckOutTime.HasValue)
            throw new DomainException("ALREADY_CHECKED_OUT", "Already checked out.");
        CheckOutTime = when ?? DateTime.UtcNow;
        DurationMinutes = Math.Max(0, (int)(CheckOutTime.Value - CheckInTime).TotalMinutes);
        WasAutoClosed = true;
        UpdatedAt = DateTime.UtcNow;
    }
}
