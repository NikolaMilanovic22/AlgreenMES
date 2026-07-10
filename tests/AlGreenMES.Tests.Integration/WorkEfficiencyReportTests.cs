using System.Net;
using System.Text.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// /api/reports/work-efficiency — per-worker per-day breakdown of Pravo
/// vreme rada / Aktivno na procesima / Pauze / Efikasnost %. Bojan spec
/// 25.05.2026; lazy auto-logout 26.05.2026 (no background job).
///
/// Aspects covered:
///   • Closed sessions with absurd durations are capped at
///     ShiftDuration + MaxOvertimeHours (bug found via curl 26.05.2026).
///   • Open sessions past the cap show up auto-closed; open sessions
///     still within bounds are excluded.
///   • Worker filter narrows results.
///   • Cross-tenant isolation.
/// </summary>
public class WorkEfficiencyReportTests : IntegrationTestBase
{
    public WorkEfficiencyReportTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task WorkEfficiency_caps_closed_session_with_absurd_duration()
    {
        // A worker checked in at 06:00, checked out 7 days later. Shift is
        // 06:00–14:00 (8h) with 6h max overtime → cap = 14h = 840 min.
        // Report should show 840m worked, NOT the raw 7-day duration.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            maxOvertimeHours: 6);

        // Pick a date a few days in the past so the session is fully in range.
        var checkIn = DateTime.UtcNow.Date.AddDays(-3).AddHours(6);
        var checkOut = checkIn.AddDays(7); // bogus — forgotten checkout

        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, checkIn, checkOut);

        var from = DateOnly.FromDateTime(checkIn).AddDays(-1);
        var to = DateOnly.FromDateTime(checkIn).AddDays(1);

        var resp = await client.GetAsync(
            $"/api/reports/work-efficiency?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var rows = doc.RootElement.GetProperty("rows").EnumerateArray().ToList();
        var row = rows.Single(r => r.GetProperty("userId").GetGuid() == t.UserId);

        // Cap = 8h shift + 6h overtime = 14h = 840 min.
        row.GetProperty("loggedMinutes").GetInt32().Should().Be(840);
    }

    [Fact]
    public async Task WorkEfficiency_open_session_past_cap_shows_as_auto_closed()
    {
        // Worker checked in days ago, never checked out. Past the 14h cap,
        // the report should treat it as auto-closed at the cap.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            maxOvertimeHours: 6);

        var checkIn = DateTime.UtcNow.Date.AddDays(-3).AddHours(6);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, checkIn, checkOutTime: null);

        var from = DateOnly.FromDateTime(checkIn).AddDays(-1);
        var to = DateOnly.FromDateTime(checkIn).AddDays(1);

        var resp = await client.GetAsync(
            $"/api/reports/work-efficiency?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var rows = doc.RootElement.GetProperty("rows").EnumerateArray().ToList();
        var row = rows.Single(r => r.GetProperty("userId").GetGuid() == t.UserId);
        row.GetProperty("loggedMinutes").GetInt32().Should().Be(840);
    }

    [Fact]
    public async Task WorkEfficiency_open_session_within_cap_is_excluded()
    {
        // Worker checked in 30 minutes ago — well within shift + overtime cap.
        // Session is still legitimately open; report must NOT include it.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        // 8h shift bracketing UtcNow + 6h maxOT → cap = checkIn+14h is in the
        // future for any test-run time-of-day, so the open session is still
        // legitimately within bounds and must be excluded from the report.
        var nowUtc = DateTime.UtcNow;
        var shiftStart = TimeOnly.FromDateTime(nowUtc.AddHours(-1));
        var shiftEnd = shiftStart.AddHours(8);
        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: shiftStart,
            endTime: shiftEnd,
            maxOvertimeHours: 6);

        var checkIn = nowUtc.AddMinutes(-30);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, checkIn, checkOutTime: null);

        var from = DateOnly.FromDateTime(checkIn);
        var to = from;
        var resp = await client.GetAsync(
            $"/api/reports/work-efficiency?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        // No row for this worker — session is open and within bounds.
        var rows = doc.RootElement.GetProperty("rows").EnumerateArray().ToList();
        rows.Should().NotContain(r => r.GetProperty("userId").GetGuid() == t.UserId);
    }

    [Fact]
    public async Task WorkEfficiency_isolates_data_across_tenants()
    {
        // Tenant A's worker is Department (so A genuinely has worker data);
        // the test proves B can't see it.
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory, roleForA: UserRole.Department);
        await TestDataSeeder.SeedShiftAsync(
            Factory, a.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0));
        var checkIn = DateTime.UtcNow.Date.AddDays(-1).AddHours(6);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, a.TenantId, a.UserId, checkIn, checkIn.AddHours(8));

        var clientB = await TestDataSeeder.AuthenticatedClientAsync(Factory, b);
        var from = DateOnly.FromDateTime(checkIn).AddDays(-1);
        var to = DateOnly.FromDateTime(checkIn).AddDays(1);
        var resp = await clientB.GetAsync(
            $"/api/reports/work-efficiency?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        doc.RootElement.GetProperty("rows").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task WorkEfficiency_unauthenticated_returns_401()
    {
        var anon = Factory.CreateClient();
        var resp = await anon.GetAsync(
            $"/api/reports/work-efficiency?from={DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}&to={DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WorkEfficiency_aggregates_one_row_per_worker_across_days()
    {
        // Excel Table 2 (29.05.2026): one row PER WORKER over the period — not
        // per day. Two 8h sessions on two days for one worker → a single row
        // with loggedMinutes = 960.
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
            $"/api/reports/work-efficiency?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var rows = doc.RootElement.GetProperty("rows").EnumerateArray()
            .Where(r => r.GetProperty("userId").GetGuid() == t.UserId)
            .ToList();
        rows.Should().HaveCount(1);
        var row = rows[0];
        row.GetProperty("loggedMinutes").GetInt32().Should().Be(960);
        row.GetProperty("effectiveMinutes").GetInt32().Should().Be(960); // shift break = 0
        row.TryGetProperty("uncoveredMinutes", out _).Should().BeTrue();
        row.TryGetProperty("efficiencyPercent", out _).Should().BeTrue();
    }

    [Fact]
    public async Task WorkEfficiency_caps_efficiency_percent_at_100()
    {
        // Saša 08.07.2026 — the Efikasnost report is Aktivno/Efektivno × 100.
        // Aktivno is raw session-active time; Efektivno subtracts the shift
        // break. A worker active for the whole session therefore has
        // active > effective and the raw ratio exceeds 100%, which isn't
        // meaningful to show. The per-worker row must cap at 100%.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            breakMinutes: 30,        // effective = worked − 30
            maxOvertimeHours: 6);

        var day = DateTime.UtcNow.Date.AddDays(-1).AddHours(6);
        // 8h30m session: worked = 510, effective = 480.
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, day, day.AddMinutes(510));

        // Process log covering the FULL session → active = 510 > effective = 480,
        // so the raw ratio would be 106.25%.
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.InProgress);
        await SeedProcessLogAsync(t.TenantId, oipId, t.UserId, day, day.AddMinutes(510));

        var from = DateOnly.FromDateTime(day).AddDays(-1);
        var to = DateOnly.FromDateTime(day).AddDays(1);
        var resp = await client.GetAsync(
            $"/api/reports/work-efficiency?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var row = doc.RootElement.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("userId").GetGuid() == t.UserId);

        // Sanity: genuinely the active>effective case.
        row.GetProperty("activeOnProcessesMinutes").GetInt32().Should().Be(510);
        row.GetProperty("effectiveMinutes").GetInt32().Should().Be(480);
        // Efficiency capped — raw would be 106.25%.
        row.GetProperty("efficiencyPercent").GetDouble().Should().Be(100.0);
    }

    /// <summary>Helper: seed an OrderItemProcessLog with explicit timestamps
    /// (mirrors the WorkerHoursReportTests helper — attributes active process
    /// time to a worker within a session).</summary>
    private async Task SeedProcessLogAsync(Guid tenantId, Guid oipId, Guid userId, DateTime start, DateTime? end)
    {
        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await ordersDb.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO orders.order_item_process_logs
                (id, order_item_process_id, user_id, tenant_id, start_time, end_time, duration_seconds, created_at)
            VALUES
                ({Guid.NewGuid()}, {oipId}, {userId}, {tenantId},
                 {start}, {end},
                 {(end.HasValue ? (int?)(end.Value - start).TotalSeconds : null)},
                 NOW())");
    }
}
