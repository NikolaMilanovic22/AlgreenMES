using System.Text.Json;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using AlGreenMES.Modules.Production.Domain.Enums;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// /api/reports/process-time-trend — single (process × complexity) trend
/// chart with two-pass robust stats: clean RAW samples to within [μ₀±σ₀],
/// then return μ′±σ′ on the cleaned subset as MIN/MAX. Bojan review
/// round 3 (27.05.2026). Rounds 1+2 (Excel MINIFS/MAXIFS, literal μ±σ
/// on raw) were both flagged wrong.
/// </summary>
public class ProcessTimeTrendTests : IntegrationTestBase
{
    public ProcessTimeTrendTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Trend_robust_min_max_uses_cleaned_mean_plus_minus_sigma()
    {
        // Samples: {5, 10, 15, 20, 25} minutes (stored as seconds:
        // {300, 600, 900, 1200, 1500}).
        // Pass 1: μ₀=15, σ₀=√50 ≈ 7.07, cleaning window [7.93, 22.07].
        // Cleaned subset: {10, 15, 20}.
        // Pass 2: μ′=15, σ′=√(50/3) ≈ 4.08.
        // MIN ≈ 10.92, MAX ≈ 19.08, trimmedMean = 15.
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
        b.GetProperty("minMinutes").GetDouble().Should().BeApproximately(10.92, 0.01);
        b.GetProperty("maxMinutes").GetDouble().Should().BeApproximately(19.08, 0.01);
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
    public async Task Trend_sub_process_sum_overrides_parent_duration_and_excludes_withdrawn()
    {
        // EffectiveDurationSeconds (ReportingQueryService ~129): when an OIP has
        // non-withdrawn sub-processes with duration > 0, the report uses the SUM
        // of those sub durations instead of the parent's own timer total. A
        // withdrawn sub is excluded entirely. Parent = 600s; two live subs
        // 150+150 = 300s (5 min); one withdrawn sub of 500s that must NOT count.
        // If the override or the withdrawn-exclusion regressed, the single-sample
        // trimmed mean would be 10 (parent) or 13.3 (incl. withdrawn), not 5.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);

        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId,
            status: ProcessStatus.Completed,
            totalDurationSeconds: 600,
            complexity: ComplexityType.S);

        // Three subs on the OIP. SeedOrderItemSubProcessAsync inserts a
        // sub_process template with sequence_order=1, which is unique per
        // process — so give each sub its own template process to avoid the
        // (process_id, sequence_order) collision. The OISP→OIP link is what the
        // report reads, so the template process is irrelevant to the math.
        var subProc1 = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var subProc2 = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var subProc3 = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        // SeedOrderItemSubProcessAsync inserts total_duration_minutes = 0
        // (seconds unit), so override via raw SQL.
        var sub1 = await TestDataSeeder.SeedOrderItemSubProcessAsync(Factory, oipId, subProc1);
        var sub2 = await TestDataSeeder.SeedOrderItemSubProcessAsync(Factory, oipId, subProc2);
        var subWithdrawn = await TestDataSeeder.SeedOrderItemSubProcessAsync(Factory, oipId, subProc3);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE orders.order_item_sub_processes SET total_duration_minutes = 150 WHERE id = {0} OR id = {1}",
                sub1, sub2);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE orders.order_item_sub_processes SET total_duration_minutes = 500, is_withdrawn = true WHERE id = {0}",
                subWithdrawn);
        }

        var resp = await client.GetAsync(
            $"/api/reports/process-time-trend?processId={processId}&complexity=S&granularity=Week");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var b = doc.RootElement.GetProperty("buckets").EnumerateArray().Single();
        b.GetProperty("count").GetInt32().Should().Be(1);
        // Effective = 150 + 150 = 300s = 5 min (withdrawn 500s excluded).
        b.GetProperty("trimmedMeanMinutes").GetDouble().Should().BeApproximately(5.0, 0.01);
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
