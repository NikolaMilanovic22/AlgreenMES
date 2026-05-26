using System.Text.Json;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// /api/reports/worker-hours — per-worker totals + daily breakdown. This
/// session added lazy auto-logout to this endpoint too (same helper as
/// Efikasnost), so the same cap behaviour applies here.
/// </summary>
public class WorkerHoursReportTests : IntegrationTestBase
{
    public WorkerHoursReportTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task WorkerHours_caps_closed_session_with_absurd_duration()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            maxOvertimeHours: 6);

        var checkIn = DateTime.UtcNow.Date.AddDays(-3).AddHours(6);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, checkIn, checkIn.AddDays(7));

        var from = DateOnly.FromDateTime(checkIn).AddDays(-1);
        var to = DateOnly.FromDateTime(checkIn).AddDays(1);

        var resp = await client.GetAsync(
            $"/api/reports/worker-hours?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var worker = doc.RootElement.GetProperty("workers").EnumerateArray()
            .Single(w => w.GetProperty("userId").GetGuid() == t.UserId);
        // Cap = 8h shift + 6h overtime = 14h = 840 min (NOT the raw 7 days).
        worker.GetProperty("totalMinutes").GetInt32().Should().Be(840);
    }

    [Fact]
    public async Task WorkerHours_returns_correct_total_for_legit_session()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            maxOvertimeHours: 6);

        var checkIn = DateTime.UtcNow.Date.AddDays(-1).AddHours(6);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, checkIn, checkIn.AddHours(8));  // legit 8h

        var from = DateOnly.FromDateTime(checkIn).AddDays(-1);
        var to = DateOnly.FromDateTime(checkIn).AddDays(1);

        var resp = await client.GetAsync(
            $"/api/reports/worker-hours?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var worker = doc.RootElement.GetProperty("workers").EnumerateArray()
            .Single(w => w.GetProperty("userId").GetGuid() == t.UserId);
        // 8h = 480 min, under the 14h cap → passes through unchanged.
        worker.GetProperty("totalMinutes").GetInt32().Should().Be(480);
    }
}
