namespace AlGreenMES.Modules.Orders.Application.DTOs.Reports;

/// <summary>
/// "Napredak aktivnih narudžbina" — per-process count of active
/// OrderItemProcesses broken into three statuses (Sale/Bojan spec
/// 22.05.2026 + clarification 23.05.2026):
///   InProgress = "U toku" (blue)
///   Ready      = "Proces spreman za izvršavanje" (gray)
///   Blocked    = "Blokirano" (red)
/// "Ready" = Pending AND every dependency complete-or-withdrawn (same
/// logic as the live drawer "spreman" indicator). Pending-but-waiting-
/// on-deps rows are NOT counted — the chart only shows rows that are
/// "actively in the pipeline" for that process.
/// </summary>
public record ActiveProcessFunnelDto(List<ActiveProcessFunnelBucketDto> Processes);

public record ActiveProcessFunnelBucketDto(
    Guid ProcessId,
    string ProcessCode,
    string ProcessName,
    int SequenceOrder,
    int InProgressCount,
    int ReadyCount,
    int BlockedCount);
