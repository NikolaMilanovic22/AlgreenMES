namespace AlGreenMES.Modules.Orders.Application.DTOs.Reports;

/// <summary>
/// Per-period (week or month) on-time vs. late breakdown.
/// "Analiza kašnjenja i poštovanja rokova" chart (Sale/Bojan spec
/// from 22.05.2026 — green = % on-time, red = % late).
/// </summary>
public record DeliveryComplianceReportDto(List<DeliveryComplianceBucketDto> Buckets);

public record DeliveryComplianceBucketDto(
    /// <summary>ISO date of bucket start (week → Monday, month → first day).</summary>
    DateTime BucketStart,
    int OnTimeCount,
    int LateCount)
{
    public int TotalCount => OnTimeCount + LateCount;
    public double OnTimePercent => TotalCount == 0 ? 0 : Math.Round(100.0 * OnTimeCount / TotalCount, 1);
    public double LatePercent => TotalCount == 0 ? 0 : Math.Round(100.0 * LateCount / TotalCount, 1);
}
