namespace AlGreenMES.Modules.Identity.Application.DTOs;

public record ShiftDto(
    Guid Id,
    Guid TenantId,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsActive,
    int BreakMinutes,
    int MaxOvertimeHours,
    int AutoLogoutAfterHours,
    int AlarmBeforeLogoutMinutes,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
