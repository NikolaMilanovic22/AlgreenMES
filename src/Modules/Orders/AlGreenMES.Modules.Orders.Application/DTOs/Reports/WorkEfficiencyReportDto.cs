namespace AlGreenMES.Modules.Orders.Application.DTOs.Reports;

/// <summary>
/// "Efikasnost radnog vremena" — per-WORKER aggregate over the filtered
/// period (Excel Table 2, Sale/Bojan 29.05.2026). One row per worker;
/// the per-day detail lives in the "Sati radnika" tab (WorkerHoursReportDto).
///
///   • LoggedMinutes      = Σ TotalWorked (Prijavljeno / ukupno).
///   • EffectiveMinutes   = Σ Effective (Efektivno).
///   • ActiveMinutes      = Σ Active na procesima.
///   • UncoveredMinutes   = Σ Nepokriveno.
///   • EfficiencyPercent  = ΣActive / ΣEffective × 100 (weighted).
///
/// Status + color are derived on the FE from EfficiencyPercent
/// (≥80 Odlično, 60–79 Prihvatljivo, 40–59 Ispod norme, &lt;40 Neprihvatljivo).
/// </summary>
public record WorkEfficiencyReportDto(List<WorkEfficiencyRowDto> Rows);

public record WorkEfficiencyRowDto(
    Guid UserId,
    string FullName,
    int LoggedMinutes,
    int EffectiveMinutes,
    int ActiveOnProcessesMinutes,
    int UncoveredMinutes,
    double EfficiencyPercent);
