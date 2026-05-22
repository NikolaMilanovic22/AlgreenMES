using AlGreenMES.Modules.Orders.Domain.Enums;

namespace AlGreenMES.Modules.Orders.Application.DTOs;

public record OrderMasterViewDto(
    Guid Id,
    string OrderNumber,
    string OrderType,
    string Status,
    DateTime DeliveryDate,
    int Priority,
    int? CustomWarningDays,
    int? CustomCriticalDays,
    int CompletedProcesses,
    int TotalProcesses,
    Dictionary<string, string> ProcessStatuses,
    Dictionary<string, int> ProcessDurations,
    Dictionary<string, bool> ProcessPaused,
    /// <summary>
    /// Map of processId → true if at least ONE item has this process Pending and
    /// all of that item's dependencies for it are Completed or Withdrawn. Computed
    /// per-item because the aggregated ProcessStatuses can't tell that one item is
    /// ready when sibling items are still mid-pipeline.
    /// </summary>
    Dictionary<string, bool> ProcessReady,
    /// <summary>
    /// Per-item readiness: itemId → (processId → true if Pending + all this item's
    /// deps for that process are Completed/Withdrawn). The FE needs this for the
    /// per-item ItemProcessBar squares — it can't compute readiness from the flat
    /// ProcessDependencies dict because that dict unions deps across categories
    /// (an item's process might not actually depend on what the dict says it does).
    /// </summary>
    Dictionary<string, Dictionary<string, bool>> ItemProcessReady,
    /// <summary>Map of processId → list of processIds it depends on</summary>
    Dictionary<string, List<string>> ProcessDependencies,
    int AttachmentCount,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    bool IsInvoiced);
