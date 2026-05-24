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
}
