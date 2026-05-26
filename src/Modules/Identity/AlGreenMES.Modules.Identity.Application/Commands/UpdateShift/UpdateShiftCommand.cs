using AlGreenMES.Modules.Identity.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Commands.UpdateShift;

public record UpdateShiftCommand(
    Guid Id,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsActive,
    int BreakMinutes,
    int MaxOvertimeHours,
    int AutoLogoutAfterHours,
    int AlarmBeforeLogoutMinutes) : IRequest<ShiftDto>;
