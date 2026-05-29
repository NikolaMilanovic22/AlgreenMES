using System.Text.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
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
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
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
        worker.GetProperty("totalWorkedMinutes").GetInt32().Should().Be(840);
    }

    [Fact]
    public async Task WorkerHours_returns_correct_total_for_legit_session()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
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
        worker.GetProperty("totalWorkedMinutes").GetInt32().Should().Be(480);
    }

    [Fact]
    public async Task WorkerHours_daily_breakdown_carries_per_day_detail()
    {
        // Sati radnika (29.05.2026): per-worker totals + a daily row carrying
        // the rich columns (regular/overtime/effective/active/uncovered + times).
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            maxOvertimeHours: 6);

        var day1 = DateTime.UtcNow.Date.AddDays(-3).AddHours(6);
        var day2 = DateTime.UtcNow.Date.AddDays(-2).AddHours(6);
        await TestDataSeeder.SeedWorkSessionAsync(Factory, t.TenantId, t.UserId, day1, day1.AddHours(8));
        await TestDataSeeder.SeedWorkSessionAsync(Factory, t.TenantId, t.UserId, day2, day2.AddHours(8));

        var from = DateOnly.FromDateTime(day1).AddDays(-1);
        var to = DateOnly.FromDateTime(day2).AddDays(1);
        var resp = await client.GetAsync(
            $"/api/reports/worker-hours?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var worker = doc.RootElement.GetProperty("workers").EnumerateArray()
            .Single(w => w.GetProperty("userId").GetGuid() == t.UserId);
        worker.GetProperty("totalWorkedMinutes").GetInt32().Should().Be(960);

        var daily = worker.GetProperty("dailyBreakdown").EnumerateArray().ToList();
        daily.Should().HaveCount(2);
        var d = daily[0];
        d.GetProperty("totalWorkedMinutes").GetInt32().Should().Be(480);
        d.TryGetProperty("regularMinutes", out _).Should().BeTrue();
        d.TryGetProperty("overtimeMinutes", out _).Should().BeTrue();
        d.TryGetProperty("effectiveMinutes", out _).Should().BeTrue();
        d.TryGetProperty("activeMinutes", out _).Should().BeTrue();
        d.TryGetProperty("uncoveredMinutes", out _).Should().BeTrue();
        d.TryGetProperty("firstCheckIn", out _).Should().BeTrue();
        d.TryGetProperty("lastCheckOut", out _).Should().BeTrue();
    }

    [Fact]
    public async Task WorkerHours_splits_regular_and_overtime_at_shift_duration()
    {
        // A 10h session on an 8h shift (cap = 8h + 6h overtime = 14h, so the
        // session is NOT capped). Regular caps at the 8h shift duration
        // (480 min); the remaining 2h (120 min) becomes overtime. No break
        // configured → effective = total worked.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            breakMinutes: 0,
            maxOvertimeHours: 6);

        var checkIn = DateTime.UtcNow.Date.AddDays(-1).AddHours(6);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, checkIn, checkIn.AddHours(10)); // 10h

        var from = DateOnly.FromDateTime(checkIn).AddDays(-1);
        var to = DateOnly.FromDateTime(checkIn).AddDays(1);
        var resp = await client.GetAsync(
            $"/api/reports/worker-hours?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var worker = doc.RootElement.GetProperty("workers").EnumerateArray()
            .Single(w => w.GetProperty("userId").GetGuid() == t.UserId);
        worker.GetProperty("totalWorkedMinutes").GetInt32().Should().Be(600);
        worker.GetProperty("regularMinutes").GetInt32().Should().Be(480);
        worker.GetProperty("overtimeMinutes").GetInt32().Should().Be(120);
        // No break → effective = worked.
        worker.GetProperty("effectiveMinutes").GetInt32().Should().Be(600);
    }

    [Fact]
    public async Task WorkerHours_excludes_non_department_users()
    {
        // Worker reports are for factory-floor (Department) staff only. An Admin
        // with a check-in session must NOT appear; a Department worker must.
        // (Confirmed by Milos 29.05.2026: only the worker role belongs here.)
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin); // admin, for auth
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId, startTime: new TimeOnly(6, 0), endTime: new TimeOnly(14, 0));

        var worker = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Department);
        var day = DateTime.UtcNow.Date.AddDays(-1).AddHours(6);
        await TestDataSeeder.SeedWorkSessionAsync(Factory, t.TenantId, t.UserId, day, day.AddHours(8)); // admin session
        await TestDataSeeder.SeedWorkSessionAsync(Factory, t.TenantId, worker, day, day.AddHours(8));    // worker session

        var from = DateOnly.FromDateTime(day).AddDays(-1);
        var to = DateOnly.FromDateTime(day).AddDays(1);
        var resp = await client.GetAsync(
            $"/api/reports/worker-hours?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var ids = doc.RootElement.GetProperty("workers").EnumerateArray()
            .Select(w => w.GetProperty("userId").GetGuid()).ToList();
        ids.Should().Contain(worker);       // Department worker present
        ids.Should().NotContain(t.UserId);  // Admin excluded
    }
}
