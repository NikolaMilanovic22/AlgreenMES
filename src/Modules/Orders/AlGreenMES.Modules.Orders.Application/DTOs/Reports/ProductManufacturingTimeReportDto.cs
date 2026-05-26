namespace AlGreenMES.Modules.Orders.Application.DTOs.Reports;

/// <summary>
/// "Prosečno trajanje izrade proizvoda" — per-completed-order
/// breakdown of process timings + inter-process gaps (Sale/Bojan
/// spec 25.05.2026). One row per order, columns wide enough to
/// fit all processes the order touched (no fixed 7-process cap —
/// spec says "Svi procesi moraju da budu prikazani").
///
/// Processes are ordered by their start time (first started = process 1).
/// If process N+1 starts before process N completes, the overlap is
/// clipped — the gap "Do sledećeg procesa" treats start of N+1 as
/// equal to end of N (no negative gaps).
///
/// Top complexity ("najzastupljenija težina") uses item-count majority
/// with low-bias tie-break per spec: T/S=S, S/L=L, T/L=L. When all
/// three appear at equal counts, falls back to L.
/// </summary>
public record ProductManufacturingTimeReportDto(List<ProductManufacturingTimeOrderDto> Orders);

public record ProductManufacturingTimeOrderDto(
    Guid OrderId,
    string OrderNumber,
    string OrderType,
    string ProductCategoryName,
    /// <summary>"T" / "S" / "L" — null if order had no items with complexity set.</summary>
    string? TopComplexity,
    List<ProductManufacturingProcessDto> Processes,
    /// <summary>Sum of all process durations + all positive inter-process gaps. Seconds.</summary>
    int TotalWithGapsSeconds,
    /// <summary>Sum of all process durations only. Seconds.</summary>
    int TotalWithoutGapsSeconds);

public record ProductManufacturingProcessDto(
    Guid ProcessId,
    string ProcessCode,
    string ProcessName,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int DurationSeconds,
    /// <summary>Gap from THIS process's end to NEXT process's start, in seconds.
    /// Zero for the last process or when the next process overlaps. </summary>
    int GapToNextSeconds);
