using AlGreenMES.Modules.Orders.Application.DTOs.Reports;

namespace AlGreenMES.Modules.Orders.Infrastructure.Services;

/// <summary>
/// Pure math helpers for /reports aggregation. Extracted from
/// ReportingQueryService so they can be unit-tested in isolation without
/// spinning up a full DbContext or HTTP host. Single responsibility:
/// given a list of samples, return the stats Sale/Bojan's Excel spec
/// describes (see Tab 1 "Vremena po procesu" + StDev sheet).
/// </summary>
public static class ReportingStats
{
    /// <summary>
    /// 1-sigma window stats per Sale/Bojan's Excel StDev sheet formula:
    ///   μ   = AVERAGE(samples)
    ///   σ   = sqrt(AVERAGE((xi−μ)²))            (population stdev)
    ///   min = MINIFS(samples, "&gt;="& μ−σ)        (smallest sample inside the band)
    ///   max = MAXIFS(samples, "&lt;="& μ+σ)        (largest sample inside the band)
    ///   trimmedMean = AVERAGEIFS(samples, "&gt;="& μ−σ, "&lt;="& μ+σ)   ("Realni prosek")
    /// min/max are window-clamped (not population min/max) — outliers excluded.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="values"/> is empty.</exception>
    public static ComplexityStatsDto ComputeStats(List<double> values)
    {
        if (values.Count == 0)
            throw new ArgumentException("ComputeStats requires at least one sample.", nameof(values));

        var n = values.Count;
        var mean = values.Average();
        var variance = values.Sum(x => (x - mean) * (x - mean)) / n;
        var stdev = Math.Sqrt(variance);

        double minWindow;
        double maxWindow;
        double trimmedMean;

        if (n == 1 || stdev == 0)
        {
            // Single sample (or all identical) — window degenerates to the
            // point itself.
            minWindow = values.Min();
            maxWindow = values.Max();
            trimmedMean = mean;
        }
        else
        {
            var lower = mean - stdev;
            var upper = mean + stdev;
            var withinWindow = values.Where(x => x >= lower && x <= upper).ToList();
            if (withinWindow.Count == 0)
            {
                // Bimodal pathology — fall back to population min/max + plain mean.
                minWindow = values.Min();
                maxWindow = values.Max();
                trimmedMean = mean;
            }
            else
            {
                minWindow = withinWindow.Min();
                maxWindow = withinWindow.Max();
                trimmedMean = withinWindow.Average();
            }
        }

        return new ComplexityStatsDto(
            n,
            Math.Round(mean, 2),
            Math.Round(minWindow, 2),
            Math.Round(maxWindow, 2),
            Math.Round(stdev, 2),
            Math.Round(trimmedMean, 2));
    }

    /// <summary>
    /// Two-pass robust stats for the Trend chart — Bojan review round 3
    /// (27.05.2026). Both the previous interpretations were "wrong" per his
    /// review:
    ///   - Excel MINIFS/MAXIFS (smallest/largest in-window sample) → MAX
    ///     was still pulled up by one borderline-in-window outlier (e.g.
    ///     PREDKROJENJE/S: MAX=46 with most samples under 5 min).
    ///   - Literal μ±σ on RAW data → exploded for any process with a single
    ///     forgotten-checkout outlier (band hit 1579 min).
    ///
    /// This pass:
    ///   1. Compute μ₀, σ₀ on RAW samples.
    ///   2. Drop everything outside [μ₀ − σ₀, μ₀ + σ₀].
    ///   3. Recompute μ′, σ′ on the cleaned subset.
    ///   4. MIN = max(0, μ′ − σ′), MAX = μ′ + σ′, TrimmedMean = μ′.
    /// Result: tight, visually-meaningful band centered on the cleaned
    /// mean; isolated outliers can't move the band.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="values"/> is empty.</exception>
    public static ComplexityStatsDto ComputeRobustTrendStats(List<double> values)
    {
        if (values.Count == 0)
            throw new ArgumentException("ComputeRobustTrendStats requires at least one sample.", nameof(values));

        var n = values.Count;
        var meanRaw = values.Average();
        var varianceRaw = values.Sum(x => (x - meanRaw) * (x - meanRaw)) / n;
        var stdevRaw = Math.Sqrt(varianceRaw);

        List<double> cleaned;
        if (n == 1 || stdevRaw == 0)
        {
            cleaned = values;
        }
        else
        {
            cleaned = values.Where(x => x >= meanRaw - stdevRaw && x <= meanRaw + stdevRaw).ToList();
            if (cleaned.Count == 0) cleaned = values; // bimodal pathology — defensive
        }

        var meanCleaned = cleaned.Average();
        var stdevCleaned = cleaned.Count > 1
            ? Math.Sqrt(cleaned.Sum(x => (x - meanCleaned) * (x - meanCleaned)) / cleaned.Count)
            : 0.0;

        var minBand = Math.Max(0, meanCleaned - stdevCleaned);
        var maxBand = meanCleaned + stdevCleaned;

        return new ComplexityStatsDto(
            n,
            Math.Round(meanCleaned, 2),
            Math.Round(minBand, 2),
            Math.Round(maxBand, 2),
            Math.Round(stdevCleaned, 2),
            Math.Round(meanCleaned, 2));
    }
}
