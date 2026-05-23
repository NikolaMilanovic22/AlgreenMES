namespace AlGreenMES.Modules.Orders.Application.DTOs.Reports;

/// <summary>
/// Per-period trend for a single (process × complexity) — Sale/Bojan
/// "Trend prosečnog vremena po nedelji" chart. The green band is
/// MIN/MAX window-clamped per bucket (same formula as the Vremena table —
/// smallest sample ≥ μ-σ, largest ≤ μ+σ). Blue line is Realni prosek
/// per bucket. The Normativ (red dashed) is 85% of the Realni prosek
/// across the WHOLE filtered period (constant horizontal line) —
/// computed BE-side so all clients agree on the same number.
/// </summary>
public record ProcessTimeTrendDto(
    List<ProcessTimeTrendBucketDto> Buckets,
    /// <summary>
    /// 85% of trimmed mean across all filtered samples — the target
    /// "Normativ (cilj)" line. Null when no samples in the period.
    /// </summary>
    double? NormativMinutes);

public record ProcessTimeTrendBucketDto(
    DateTime BucketStart,
    int Count,
    /// <summary>Realni prosek (trimmed mean) of this bucket's samples, in minutes.</summary>
    double TrimmedMeanMinutes,
    /// <summary>Smallest sample inside μ±σ window, in minutes.</summary>
    double MinMinutes,
    /// <summary>Largest sample inside μ±σ window, in minutes.</summary>
    double MaxMinutes);
