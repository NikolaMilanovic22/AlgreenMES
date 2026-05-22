namespace AlGreenMES.Modules.Orders.Application.DTOs;

public record OrderTypeDto(
    Guid Id,
    string Code,
    string Name,
    bool AllowsManualProcesses,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
