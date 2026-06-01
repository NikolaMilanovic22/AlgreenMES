namespace AlGreenMES.Modules.Orders.Application.DTOs;

public record WorkSessionDto(
    Guid Id,
    Guid UserId,
    DateTime CheckInTime,
    DateTime? CheckOutTime,
    int? DurationMinutes,
    DateOnly Date,
    bool IsActive,
    bool WasAutoClosed);
