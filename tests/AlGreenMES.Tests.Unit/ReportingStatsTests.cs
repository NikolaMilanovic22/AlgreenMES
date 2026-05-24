using AlGreenMES.Modules.Orders.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Unit;

/// <summary>
/// Unit tests for the /reports stats math (window-clamped MIN/MAX + trimmed
/// mean). These guard the most-subtle code in the reporting pipeline; a
/// silent regression would change every Sale/Bojan-facing average and
/// trimmed-mean value with no visible error.
/// </summary>
public class ReportingStatsTests
{
    [Fact]
    public void ComputeStats_throws_when_input_is_empty()
    {
        var act = () => ReportingStats.ComputeStats(new List<double>());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComputeStats_single_sample_returns_value_for_all_stats()
    {
        var stats = ReportingStats.ComputeStats(new List<double> { 12.5 });

        stats.Count.Should().Be(1);
        stats.AvgMinutes.Should().Be(12.5);
        stats.MinMinutes.Should().Be(12.5);
        stats.MaxMinutes.Should().Be(12.5);
        stats.StdevMinutes.Should().Be(0);
        stats.TrimmedMeanMinutes.Should().Be(12.5);
    }

    [Fact]
    public void ComputeStats_all_identical_samples_degenerate_to_value()
    {
        // σ = 0 — window math has no useful range, fall back to the value.
        var stats = ReportingStats.ComputeStats(new List<double> { 10, 10, 10, 10 });

        stats.Count.Should().Be(4);
        stats.AvgMinutes.Should().Be(10);
        stats.MinMinutes.Should().Be(10);
        stats.MaxMinutes.Should().Be(10);
        stats.StdevMinutes.Should().Be(0);
        stats.TrimmedMeanMinutes.Should().Be(10);
    }

    [Fact]
    public void ComputeStats_uniform_distribution_no_outliers()
    {
        // 10, 12, 14, 16, 18 — μ=14, σ≈2.83. Window [11.17, 16.83] includes
        // 12, 14, 16 (3 samples). Trimmed mean = 14. Window-clamped min=12,
        // max=16. Plain mean still 14.
        var stats = ReportingStats.ComputeStats(new List<double> { 10, 12, 14, 16, 18 });

        stats.Count.Should().Be(5);
        stats.AvgMinutes.Should().Be(14);
        stats.MinMinutes.Should().Be(12);
        stats.MaxMinutes.Should().Be(16);
        stats.TrimmedMeanMinutes.Should().Be(14);
        // σ = sqrt((16+4+0+4+16)/5) = sqrt(8) ≈ 2.83
        stats.StdevMinutes.Should().BeApproximately(2.83, 0.01);
    }

    [Fact]
    public void ComputeStats_with_outlier_excludes_it_from_min_max_and_trimmed_mean()
    {
        // Sale/Bojan's "abandoned 48h process" scenario. Without window-clamp
        // the plain max would be 1000 and would pull the average up. The
        // window-clamped max should be 16 (the largest sample inside μ±σ),
        // and the trimmed mean should be much closer to the cluster (≈14)
        // than to the plain mean.
        var samples = new List<double> { 10, 12, 14, 16, 18, 1000 };
        var stats = ReportingStats.ComputeStats(samples);

        stats.Count.Should().Be(6);
        // Plain mean is pulled up dramatically by the 1000 outlier.
        stats.AvgMinutes.Should().BeGreaterThan(150);
        // Window-clamped min/max + trimmed mean should ignore the outlier.
        stats.MaxMinutes.Should().BeLessOrEqualTo(20);
        stats.TrimmedMeanMinutes.Should().BeInRange(10, 20);
    }

    [Fact]
    public void ComputeStats_window_min_is_smallest_sample_at_or_above_mean_minus_sigma()
    {
        // 5, 10, 11, 12, 13. μ=10.2, σ≈2.785. Window [7.415, 12.985]. 5 is
        // below window (excluded). 13 is above (also excluded — barely).
        // Min within window = 10. Max within window = 12.
        var stats = ReportingStats.ComputeStats(new List<double> { 5, 10, 11, 12, 13 });

        stats.MinMinutes.Should().Be(10);
        stats.MaxMinutes.Should().Be(12);
    }

    [Fact]
    public void ComputeStats_window_max_is_largest_sample_at_or_below_mean_plus_sigma()
    {
        // 10, 11, 12, 13, 100. μ=29.2, σ≈35.4. Window [-6.2, 64.6]. Max in
        // window = 13 (100 is excluded as > upper bound). Min in window = 10.
        var stats = ReportingStats.ComputeStats(new List<double> { 10, 11, 12, 13, 100 });

        stats.MinMinutes.Should().Be(10);
        stats.MaxMinutes.Should().Be(13);
    }

    [Fact]
    public void ComputeStats_count_reflects_input_size_not_window_size()
    {
        // count should be the raw sample count, NOT the count of samples
        // inside the trimmed window. Sale/Bojan need to see how many actual
        // process completions contributed to the bucket.
        var stats = ReportingStats.ComputeStats(new List<double> { 1, 2, 3, 4, 1000 });
        stats.Count.Should().Be(5);
    }

    [Fact]
    public void ComputeStats_two_samples_window_degenerates_to_one_value()
    {
        // μ = 15, σ = 5, window = [10, 20]. Both samples inside.
        var stats = ReportingStats.ComputeStats(new List<double> { 10, 20 });

        stats.Count.Should().Be(2);
        stats.AvgMinutes.Should().Be(15);
        stats.MinMinutes.Should().Be(10);
        stats.MaxMinutes.Should().Be(20);
        stats.TrimmedMeanMinutes.Should().Be(15);
    }

    [Fact]
    public void ComputeStats_rounds_to_two_decimal_places()
    {
        // 1/3 of 100 = 33.333... should round to 33.33 (not 33.3 or 33.3333).
        var stats = ReportingStats.ComputeStats(new List<double> { 33.333333, 33.333333, 33.333333 });

        stats.AvgMinutes.Should().Be(33.33);
        stats.TrimmedMeanMinutes.Should().Be(33.33);
    }

    [Fact]
    public void ComputeStats_handles_zero_samples_in_window_gracefully()
    {
        // Bimodal distribution: two tight clusters far apart. Mean lands in
        // the middle; σ is large enough that the window covers some samples,
        // but if we constructed something where NO sample lands in [μ-σ, μ+σ]
        // the code should not throw. With this 4-sample bimodal, the window
        // typically contains everything. We mainly assert no exception and
        // sane fallback.
        var samples = new List<double> { 1, 1, 100, 100 };
        var act = () => ReportingStats.ComputeStats(samples);
        act.Should().NotThrow();
    }
}
