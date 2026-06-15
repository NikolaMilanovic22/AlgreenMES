namespace AlGreenMES.Modules.Orders.Application.DTOs.Reports;

/// <summary>
/// "Prosečno trajanje izrade proizvoda" — per-completed-order-ITEM
/// breakdown of process timings + inter-process gaps (Sale/Bojan
/// spec 25.05.2026, refined 29.05.2026). One row per ORDER ITEM
/// ("sve stavke iz narudžbine treba da budu prikazane"); columns wide
/// enough to fit all processes the item touched.
///
/// Processes are ordered by the canonical process sequence so the
/// columns match the order-detail table. The gap "Do sledećeg procesa"
/// = max(0, next.Start − this.Stop): out-of-order / overlapping
/// processes give 0 (no negative gaps). Computing gaps per item — not
/// per order — is what fixed the impossible 0:00:00 gaps that the old
/// per-order aggregation produced for multi-item orders.
///
/// Top complexity ("najzastupljenija težina") uses per-item process-count
/// majority with low-bias tie-break per spec: T/S=S, S/L=L, T/L=L. When
/// all three appear at equal counts, falls back to L.
/// </summary>
public record ProductManufacturingTimeReportDto(List<ProductManufacturingTimeOrderDto> Orders);

public record ProductManufacturingTimeOrderDto(
    Guid OrderId,
    Guid OrderItemId,
    string OrderNumber,
    string OrderType,
    string ProductCategoryName,
    /// <summary>"T" / "S" / "L" — null if the item had no processes with complexity set.</summary>
    string? TopComplexity,
    /// <summary>"Zastupljenost težina" — T/S/L distribution, e.g. "60% / 20% / 20%". Null if no complexity set.</summary>
    string? ComplexityShare,
    List<ProductManufacturingProcessDto> Processes,
    /// <summary>Sum of all process durations + all positive inter-process gaps. Seconds.</summary>
    int TotalWithGapsSeconds,
    /// <summary>Sum of all process durations only. Seconds.</summary>
    int TotalWithoutGapsSeconds);

public record ProductManufacturingProcessDto(
    Guid ProcessId,
    string ProcessCode,
    string ProcessName,
    /// <summary>Canonical process sequence (so the FE can order columns consistently).</summary>
    int SequenceOrder,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int DurationSeconds,
    /// <summary>Gap from THIS process's end to NEXT process's start, in seconds.
    /// Zero for the last process or when the next process overlaps. </summary>
    int GapToNextSeconds);
