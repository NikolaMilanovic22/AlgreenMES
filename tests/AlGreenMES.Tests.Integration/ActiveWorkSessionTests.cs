using System.Net;
using System.Text.Json;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// /api/work-sessions/current — calling worker's open session + auto-logout
/// alarm timestamps (driven by tablet countdown banner). Bojan spec
/// 25.05.2026, lazy approach 26.05.2026.
/// </summary>
public class ActiveWorkSessionTests : IntegrationTestBase
{
    public ActiveWorkSessionTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Current_returns_204_when_worker_has_no_open_session()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.GetAsync("/api/work-sessions/current");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Current_returns_session_with_alarm_and_logout_timestamps()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        // 8h shift, 6h max overtime, 5 min alarm → cap = 14h, alarm = 14h − 5m.
        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            maxOvertimeHours: 6,
            alarmBeforeLogoutMinutes: 5);

        // Check-in time at 06:30 (within shift) of today.
        var today = DateTime.UtcNow.Date;
        var checkIn = today.AddHours(6).AddMinutes(30);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, checkIn, checkOutTime: null);

        var resp = await client.GetAsync("/api/work-sessions/current");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var alarm = doc.RootElement.GetProperty("alarmAtUtc").GetDateTime();
        var logout = doc.RootElement.GetProperty("logoutAtUtc").GetDateTime();

        // logoutAt = CheckIn + 8h shift + 6h overtime = +14h
        logout.Should().BeCloseTo(checkIn.AddHours(14), TimeSpan.FromSeconds(2));
        // alarmAt = logoutAt − 5 minutes
        alarm.Should().BeCloseTo(logout.AddMinutes(-5), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Current_returns_null_timestamps_when_no_shift_matches()
    {
        // Worker checked in at 18:00 — no shift configured for that window.
        // BE returns session but null alarm/logout (can't cap without config).
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0));

        var today = DateTime.UtcNow.Date;
        var checkIn = today.AddHours(18); // outside the 06:00–14:00 shift
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, checkIn, checkOutTime: null);

        var resp = await client.GetAsync("/api/work-sessions/current");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        doc.RootElement.GetProperty("alarmAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("logoutAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Current_unauthenticated_returns_401()
    {
        var anon = Factory.CreateClient();
        var resp = await anon.GetAsync("/api/work-sessions/current");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
