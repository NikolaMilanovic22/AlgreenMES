using AlGreenMES.Modules.Identity.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Commands.CreateShift;

public record CreateShiftCommand(
    Guid TenantId,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes,
    int MaxOvertimeHours,
    int AutoLogoutAfterHours,
    int AlarmBeforeLogoutMinutes) : IRequest<ShiftDto>;
