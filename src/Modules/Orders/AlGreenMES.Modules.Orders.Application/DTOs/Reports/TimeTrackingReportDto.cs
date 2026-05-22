namespace AlGreenMES.Modules.Orders.Application.DTOs.Reports;

public record TimeTrackingReportDto(List<TimeTrackingItemDto> Items);

public record TimeTrackingItemDto(
    Guid OrderItemProcessId,
    string OrderNumber,
    string ProductCategoryName,
    string OrderType,
    Guid ProcessId,
    string ProcessCode,
    string ProcessName,
    string? Complexity,
    string Status,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int DurationSeconds,
    List<SubProcessTimeDto> SubProcesses);

public record SubProcessTimeDto(
    Guid SubProcessId,
    string Name,
    int DurationSeconds);
