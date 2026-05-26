using AlGreenMES.BuildingBlocks.Common.Entities;
using AlGreenMES.BuildingBlocks.Common.Exceptions;

namespace AlGreenMES.Modules.Identity.Domain.Entities;

public class
    Shift : AuditableEntity
{
    public string Name { get; private set; } = null!;
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public bool IsActive { get; private set; }

    // Per-shift time-tracking config (Bojan spec 25.05.2026).
    // Drives the Efikasnost radnog vremena report + later auto-logout job.
    public int BreakMinutes { get; private set; }
    public int MaxOvertimeHours { get; private set; }
    public int AutoLogoutAfterHours { get; private set; }
    public int AlarmBeforeLogoutMinutes { get; private set; }

    private Shift()
    {
    }

    public static Shift Create(
        Guid tenantId,
        string name,
        TimeOnly startTime,
        TimeOnly endTime,
        int breakMinutes,
        int maxOvertimeHours,
        int autoLogoutAfterHours,
        int alarmBeforeLogoutMinutes)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("SHIFT_NAME_REQUIRED", "Shift name is required.");

        ValidateConfig(breakMinutes, maxOvertimeHours, autoLogoutAfterHours, alarmBeforeLogoutMinutes);

        return new Shift
        {
            TenantId = tenantId,
            Name = name.Trim(),
            StartTime = startTime,
            EndTime = endTime,
            IsActive = true,
            BreakMinutes = breakMinutes,
            MaxOvertimeHours = maxOvertimeHours,
            AutoLogoutAfterHours = autoLogoutAfterHours,
            AlarmBeforeLogoutMinutes = alarmBeforeLogoutMinutes
        };
    }

    public void Update(
        string name,
        TimeOnly startTime,
        TimeOnly endTime,
        bool isActive,
        int breakMinutes,
        int maxOvertimeHours,
        int autoLogoutAfterHours,
        int alarmBeforeLogoutMinutes)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("SHIFT_NAME_REQUIRED", "Shift name is required.");

        ValidateConfig(breakMinutes, maxOvertimeHours, autoLogoutAfterHours, alarmBeforeLogoutMinutes);

        Name = name.Trim();
        StartTime = startTime;
        EndTime = endTime;
        IsActive = isActive;
        BreakMinutes = breakMinutes;
        MaxOvertimeHours = maxOvertimeHours;
        AutoLogoutAfterHours = autoLogoutAfterHours;
        AlarmBeforeLogoutMinutes = alarmBeforeLogoutMinutes;
    }

    private static void ValidateConfig(
        int breakMinutes,
        int maxOvertimeHours,
        int autoLogoutAfterHours,
        int alarmBeforeLogoutMinutes)
    {
        if (breakMinutes < 0)
            throw new DomainException("SHIFT_BREAK_INVALID", "Break minutes must be ≥ 0.");
        if (maxOvertimeHours < 0)
            throw new DomainException("SHIFT_OVERTIME_INVALID", "Max overtime hours must be ≥ 0.");
        if (autoLogoutAfterHours <= 0)
            throw new DomainException("SHIFT_AUTOLOGOUT_INVALID", "Auto-logout interval must be > 0 hours.");
        if (alarmBeforeLogoutMinutes < 0)
            throw new DomainException("SHIFT_ALARM_INVALID", "Alarm minutes must be ≥ 0.");
    }
}
