using System.Text.Json;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Production.Domain.Enums;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// /api/reports/process-time-trend — single (process × complexity) trend
/// chart with window-clamped MIN/MAX matching Excel's MINIFS/MAXIFS
/// (smallest/largest sample inside μ±σ band). Bojan spec 25.05.2026.
///
/// Why this code needs tests: the math has been touched twice this session
/// — first switched to literal μ±σ, then reverted to window-clamped after
/// data review. A silent regression here would change every Realni prosek
/// and band value with no visible error.
/// </summary>
public class ProcessTimeTrendTests : IntegrationTestBase
{
    public ProcessTimeTrendTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Trend_window_clamped_min_max_picks_smallest_and_largest_in_band()
    {
        // Samples: {5, 10, 15, 20, 25} minutes  (durations stored as seconds:
        // {300, 600, 900, 1200, 1500}). μ=15, σ=√50 ≈ 7.07, band ≈ [7.93, 22.07].
        // In-window samples: {10, 15, 20}. MIN=10, MAX=20, trimmedMean=15.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);

        foreach (var secs in new[] { 300, 600, 900, 1200, 1500 })
        {
            await TestDataSeeder.SeedOrderItemProcessAsync(
                Factory, t.TenantId, t.UserId, processId, categoryId,
                status: ProcessStatus.Completed,
                totalDurationSeconds: secs,
                complexity: ComplexityType.S);
        }

        var resp = await client.GetAsync(
            $"/api/reports/process-time-trend?processId={processId}&complexity=S&granularity=Week");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var buckets = doc.RootElement.GetProperty("buckets").EnumerateArray().ToList();
        // All seeded "now" → same week bucket.
        buckets.Should().HaveCount(1);
        var b = buckets[0];
        b.GetProperty("count").GetInt32().Should().Be(5);
        b.GetProperty("minMinutes").GetDouble().Should().BeApproximately(10.0, 0.01);
        b.GetProperty("maxMinutes").GetDouble().Should().BeApproximately(20.0, 0.01);
        b.GetProperty("trimmedMeanMinutes").GetDouble().Should().BeApproximately(15.0, 0.01);
    }

    [Fact]
    public async Task Trend_outlier_outside_band_is_excluded_from_min_max()
    {
        // Four 10-min samples + one wild 1000-min outlier. The outlier blows
        // up σ but stays outside the μ-σ window itself; MIN/MAX should only
        // see the cluster, not the outlier.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);

        foreach (var secs in new[] { 600, 600, 600, 600, 60_000 })
        {
            await TestDataSeeder.SeedOrderItemProcessAsync(
                Factory, t.TenantId, t.UserId, processId, categoryId,
                status: ProcessStatus.Completed,
                totalDurationSeconds: secs,
                complexity: ComplexityType.S);
        }

        var resp = await client.GetAsync(
            $"/api/reports/process-time-trend?processId={processId}&complexity=S&granularity=Week");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var b = doc.RootElement.GetProperty("buckets").EnumerateArray().Single();
        // 1000-min outlier excluded; band collapses around the 10-min cluster.
        b.GetProperty("minMinutes").GetDouble().Should().BeApproximately(10.0, 0.01);
        b.GetProperty("maxMinutes").GetDouble().Should().BeApproximately(10.0, 0.01);
        b.GetProperty("trimmedMeanMinutes").GetDouble().Should().BeApproximately(10.0, 0.01);
    }

    [Fact]
    public async Task Trend_single_sample_collapses_min_max_to_value()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);

        await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId,
            status: ProcessStatus.Completed,
            totalDurationSeconds: 720,  // 12 min
            complexity: ComplexityType.S);

        var resp = await client.GetAsync(
            $"/api/reports/process-time-trend?processId={processId}&complexity=S&granularity=Week");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var b = doc.RootElement.GetProperty("buckets").EnumerateArray().Single();
        b.GetProperty("count").GetInt32().Should().Be(1);
        b.GetProperty("minMinutes").GetDouble().Should().BeApproximately(12.0, 0.01);
        b.GetProperty("maxMinutes").GetDouble().Should().BeApproximately(12.0, 0.01);
        b.GetProperty("trimmedMeanMinutes").GetDouble().Should().BeApproximately(12.0, 0.01);
    }

    [Fact]
    public async Task Trend_normativ_is_85_percent_of_trimmed_mean()
    {
        // Same {10, 10, 10, 10, 1000} samples as the outlier test — overall
        // trimmed mean (band-filtered) = 10. Normativ = 10 × 0.85 = 8.5.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);

        foreach (var secs in new[] { 600, 600, 600, 600, 60_000 })
        {
            await TestDataSeeder.SeedOrderItemProcessAsync(
                Factory, t.TenantId, t.UserId, processId, categoryId,
                status: ProcessStatus.Completed,
                totalDurationSeconds: secs,
                complexity: ComplexityType.S);
        }

        var resp = await client.GetAsync(
            $"/api/reports/process-time-trend?processId={processId}&complexity=S&granularity=Week");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        doc.RootElement.GetProperty("normativMinutes").GetDouble().Should().BeApproximately(8.5, 0.01);
    }

    [Fact]
    public async Task Trend_no_samples_returns_empty_buckets_and_null_normativ()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);

        var resp = await client.GetAsync(
            $"/api/reports/process-time-trend?processId={processId}&complexity=S&granularity=Week");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        doc.RootElement.GetProperty("buckets").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("normativMinutes").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
