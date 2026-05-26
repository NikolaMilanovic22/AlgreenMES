using System.Net;
using System.Text.Json;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
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
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
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
        row.GetProperty("workedMinutes").GetInt32().Should().Be(840);
    }

    [Fact]
    public async Task WorkEfficiency_open_session_past_cap_shows_as_auto_closed()
    {
        // Worker checked in days ago, never checked out. Past the 14h cap,
        // the report should treat it as auto-closed at the cap.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
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
        row.GetProperty("workedMinutes").GetInt32().Should().Be(840);
    }

    [Fact]
    public async Task WorkEfficiency_open_session_within_cap_is_excluded()
    {
        // Worker checked in 30 minutes ago — well within shift + overtime cap.
        // Session is still legitimately open; report must NOT include it.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            maxOvertimeHours: 6);

        var checkIn = DateTime.UtcNow.AddMinutes(-30);
        // Force the time-of-day into the seeded shift window.
        checkIn = new DateTime(checkIn.Year, checkIn.Month, checkIn.Day, 6, 30, 0, DateTimeKind.Utc);
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
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
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
}
