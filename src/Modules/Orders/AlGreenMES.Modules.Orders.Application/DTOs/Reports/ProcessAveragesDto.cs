namespace AlGreenMES.Modules.Orders.Application.DTOs.Reports;

public record ProcessTimesDto(List<ProcessTimeItemDto> Processes);

public record ProcessTimeItemDto(
    Guid ProcessId,
    string ProcessCode,
    string ProcessName,
    Dictionary<string, ComplexityStatsDto> Stats);

public record ComplexityStatsDto(
    int Count,
    double AvgMinutes,
    double MinMinutes,
    double MaxMinutes,
    double StdevMinutes,
    double TrimmedMeanMinutes);
