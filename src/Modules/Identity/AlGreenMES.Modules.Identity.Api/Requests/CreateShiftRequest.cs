namespace AlGreenMES.Modules.Identity.Api.Requests;

public record CreateShiftRequest(
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes,
    int MaxOvertimeHours,
    int AutoLogoutAfterHours,
    int AlarmBeforeLogoutMinutes);
