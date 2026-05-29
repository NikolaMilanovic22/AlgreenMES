namespace AlGreenMES.Modules.Orders.Application.DTOs.Reports;

/// <summary>
/// "Sati radnika" — per-worker, per-DAY breakdown (Excel "Efikasnost
/// radnog vremena" Table 1, Sale/Bojan 29.05.2026). One row per worker
/// per day, plus per-worker totals used for the subtotal row and for the
/// "Efikasnost radnog vremena" tab's per-worker table + charts.
///
/// Per-day calculation (assumptions flagged for Sale/Bojan — see temp
/// questions file):
///   • A day combines ALL of the worker's sessions that day.
///   • TotalWorked = Σ session durations (lazy auto-logout cap applied).
///   • Regular = min(TotalWorked, shift duration); Overtime = the rest.
///   • Effective = TotalWorked − break (the matched shift's BreakMinutes).
///   • Active = wall-clock UNION of sub-process logs (parallel work once).
///   • Uncovered = max(0, Effective − Active).
///   • Efficiency% = Active / Effective × 100.
/// Per-worker totals sum the daily columns; efficiency is re-derived as
/// ΣActive / ΣEffective (weighted, not an average of percentages).
/// </summary>
public record WorkerHoursReportDto(List<WorkerHoursSummaryDto> Workers);

public record WorkerHoursSummaryDto(
    Guid UserId,
    string FullName,
    int RegularMinutes,
    int OvertimeMinutes,
    int TotalWorkedMinutes,
    int EffectiveMinutes,
    int ActiveMinutes,
    int UncoveredMinutes,
    double EfficiencyPercent,
    List<WorkerHoursDayDto> DailyBreakdown);

public record WorkerHoursDayDto(
    DateOnly Date,
    DateTime? FirstCheckIn,
    DateTime? LastCheckOut,
    int RegularMinutes,
    int OvertimeMinutes,
    int TotalWorkedMinutes,
    int EffectiveMinutes,
    int ActiveMinutes,
    int UncoveredMinutes,
    double EfficiencyPercent,
    int SessionCount);
