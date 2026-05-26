namespace AlGreenMES.Modules.Orders.Application.DTOs.Reports;

/// <summary>
/// "Blokade po procesu" — per-process block-request analysis (Sale/Bojan
/// spec 24.05.2026). Duration is in WORKING HOURS only (intersection of
/// CreatedAt → HandledAt with the union of all active Shift windows for
/// the tenant). Rejected blocks count toward "submitted" but contribute
/// zero duration (excluded from the average).
/// </summary>
public record BlocksPerProcessReportDto(List<BlocksPerProcessBucketDto> Processes);

public record BlocksPerProcessBucketDto(
    Guid ProcessId,
    string ProcessCode,
    string ProcessName,
    int SequenceOrder,
    /// <summary>Total submitted, regardless of status.</summary>
    int TotalSubmitted,
    /// <summary>Approved + Resolved.</summary>
    int ApprovedCount,
    /// <summary>Just Resolved (handled blocks with positive duration).</summary>
    int ResolvedCount,
    int RejectedCount,
    /// <summary>Average working-hours duration across Approved+Resolved
    /// blocks. Zero if no approved blocks.</summary>
    double AverageDurationHours);
