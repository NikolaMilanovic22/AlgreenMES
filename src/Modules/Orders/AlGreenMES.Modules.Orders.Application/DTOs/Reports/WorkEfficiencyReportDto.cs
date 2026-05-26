namespace AlGreenMES.Modules.Orders.Application.DTOs.Reports;

/// <summary>
/// "Efikasnost radnog vremena" — per-worker, per-day breakdown of
/// time spent at work vs. time actively working on processes
/// (Bojan spec 25.05.2026).
///
/// Calculations:
///   • Pravo vreme rada = sum(CheckOut − CheckIn) across all WorkSessions
///                        for that worker on that day.
///   • Aktivno na procesima = WALL-CLOCK UNION of all
///     OrderItemSubProcessLog [StartTime, EndTime] ranges for that worker
///     on that day (parallel sub-processes overlap into the same wall-clock
///     window — counted once, NOT summed — per Bojan).
///   • Pauze = max(0, PravoVremeRada − AktivnoNaProcesima).
///   • Efikasnost % = (Aktivno / PravoVremeRada) × 100.
///
/// Color coding (FE): efficiency ≥ 80 → green, 50–80 → yellow, &lt; 50 → red.
/// </summary>
public record WorkEfficiencyReportDto(List<WorkEfficiencyRowDto> Rows);

public record WorkEfficiencyRowDto(
    Guid UserId,
    string FullName,
    DateOnly Date,
    int WorkedMinutes,
    int ActiveOnProcessesMinutes,
    int BreakMinutes,
    double EfficiencyPercent);
