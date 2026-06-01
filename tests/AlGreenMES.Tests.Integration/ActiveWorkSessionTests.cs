using System.Net;
using System.Net.Http.Json;
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

        // 8h shift starting 1h before UtcNow + 6h max OT → cap = checkIn+14h
        // (well in the future, so the lazy safety net added 30.05.2026 doesn't
        // trigger). Shift bracket UtcNow so the check-in always matches.
        var nowUtc = DateTime.UtcNow;
        var checkIn = nowUtc.AddMinutes(-30);
        var shiftStart = TimeOnly.FromDateTime(nowUtc.AddHours(-1));
        var shiftEnd = shiftStart.AddHours(8);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: shiftStart,
            endTime: shiftEnd,
            maxOvertimeHours: 6,
            alarmBeforeLogoutMinutes: 5);

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

    [Fact]
    public async Task AutoCheckout_closes_session_and_marks_was_auto_closed()
    {
        // Bojan 30.05.2026 follow-up — tablet calls /auto-checkout when the
        // auto-logout cap expires. Endpoint closes the open session, sets
        // WasAutoClosed=true, and writes a non-null DurationMinutes. Same
        // contract as /check-out except for the flag.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var checkIn = DateTime.UtcNow.AddMinutes(-30);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, checkIn, checkOutTime: null);

        var resp = await client.PostAsJsonAsync(
            "/api/work-sessions/auto-checkout", new { userId = t.UserId });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        doc.RootElement.GetProperty("wasAutoClosed").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("isActive").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("checkOutTime").ValueKind.Should().NotBe(JsonValueKind.Null);
        doc.RootElement.GetProperty("durationMinutes").GetInt32().Should().BeGreaterThanOrEqualTo(29);
    }

    [Fact]
    public async Task Current_uses_overtime_cap_when_today_already_has_auto_closed_session()
    {
        // Bojan 30.05.2026 — once a worker has been auto-logged-out today, any
        // new session is an overtime re-login. The cap shifts from
        // AutoLogoutRegularMinutes to AutoLogoutAfterHours × 60 (e.g. 2h per
        // overtime session).
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        // Shift bracketed around UtcNow so check-in matches; regular cap 8.5h,
        // overtime per-session cap 2h.
        var nowUtc = DateTime.UtcNow;
        var shiftStart = TimeOnly.FromDateTime(nowUtc.AddHours(-9));
        var shiftEnd = shiftStart.AddHours(8);
        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: shiftStart,
            endTime: shiftEnd,
            maxOvertimeHours: 6,
            autoLogoutAfterHours: 2,
            autoLogoutRegularMinutes: 510); // 8.5h

        // Earlier today: a regular session that was auto-closed (the worker hit
        // the 8.5h cap).
        var earlierCheckIn = nowUtc.AddHours(-9);
        var earlierCheckOut = earlierCheckIn.AddMinutes(510); // 8.5h
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, earlierCheckIn, earlierCheckOut, wasAutoClosed: true);

        // Just now: worker re-logged in for overtime.
        var overtimeCheckIn = nowUtc.AddMinutes(-1);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, overtimeCheckIn, checkOutTime: null);

        var resp = await client.GetAsync("/api/work-sessions/current");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var logout = doc.RootElement.GetProperty("logoutAtUtc").GetDateTime();
        // Overtime cap = 2h from THIS session's check-in, NOT the 8.5h regular cap.
        logout.Should().BeCloseTo(overtimeCheckIn.AddHours(2), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Current_lazy_safety_net_auto_closes_expired_open_session()
    {
        // Bojan 30.05.2026 — tablet may go offline. The next /current poll
        // (from anyone) lazily auto-closes any open session whose logoutAt has
        // already passed. The endpoint returns 204; the persisted session is
        // marked WasAutoClosed=true with checkOutTime backdated to logoutAt.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        // 8h shift + 6h max OT → cap = checkIn + 14h.
        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            maxOvertimeHours: 6);

        // Check-in 16h ago at 06:00 (matches shift). Cap = 06:00 + 14h, well
        // in the past → expired.
        var checkIn = DateTime.UtcNow.AddDays(-1).Date.AddHours(6);
        var sessionId = await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, checkIn, checkOutTime: null);

        var resp = await client.GetAsync("/api/work-sessions/current");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // A second poll should still be 204 (session already closed).
        var resp2 = await client.GetAsync("/api/work-sessions/current");
        resp2.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AutoCheckout_creates_notification_for_tenant_dashboard_users()
    {
        // Bojan 30.05.2026 — coordinator gets a warning when auto-logout fires.
        // Every dashboard user in the tenant (Admin/Manager/Coordinator/Sales)
        // gets a WorkerAutoLoggedOut Notification; the worker themselves doesn't.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory); // Admin
        var coordinatorId = await TestDataSeeder.SeedAdditionalUserAsync(
            Factory, t.TenantId, UserRole.Coordinator);
        var workerId = await TestDataSeeder.SeedAdditionalUserAsync(
            Factory, t.TenantId, UserRole.Department);

        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var checkIn = DateTime.UtcNow.AddMinutes(-30);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, workerId, checkIn, checkOutTime: null);

        var resp = await client.PostAsJsonAsync(
            "/api/work-sessions/auto-checkout", new { userId = workerId });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var notifs = await db.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.TenantId == t.TenantId
                && n.Type == NotificationType.WorkerAutoLoggedOut)
            .ToListAsync();

        var notifUserIds = notifs.Select(n => n.UserId).ToList();
        notifUserIds.Should().Contain(t.UserId);       // Admin
        notifUserIds.Should().Contain(coordinatorId);  // Coordinator
        notifUserIds.Should().NotContain(workerId);    // Worker excluded (the warned-about, not warned)
    }

    [Fact]
    public async Task Current_lazy_safety_net_backdates_checkOutTime_to_logoutAt()
    {
        // The safety net auto-closes via AutoCheckOutCommand passing logoutAt
        // as `when`. The recorded CheckOutTime should match the cap moment, not
        // the polling time — so reports / coordinator timeline reflect when
        // the cap actually expired.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        // 8h shift bracketing checkIn, AutoLogoutRegularMinutes=60 (1h cap).
        var nowUtc = DateTime.UtcNow;
        var shiftStart = TimeOnly.FromDateTime(nowUtc.AddHours(-2));
        var shiftEnd = shiftStart.AddHours(8);
        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: shiftStart,
            endTime: shiftEnd,
            autoLogoutRegularMinutes: 60);

        var checkIn = nowUtc.AddMinutes(-90); // 90 min ago → past the 60 min cap
        var sessionId = await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, checkIn, checkOutTime: null);

        var resp = await client.GetAsync("/api/work-sessions/current");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var session = await db.WorkSessions.IgnoreQueryFilters()
            .SingleAsync(ws => ws.Id == sessionId);
        session.WasAutoClosed.Should().BeTrue();
        session.CheckOutTime.Should().NotBeNull();
        // checkOutTime should be ~checkIn + 60min (cap), NOT ~nowUtc.
        var expectedCheckOut = checkIn.AddMinutes(60);
        session.CheckOutTime!.Value.Should().BeCloseTo(expectedCheckOut, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Current_does_not_apply_overtime_cap_for_auto_closed_session_from_yesterday()
    {
        // The "is this an OT re-login?" query filters by Date == today, so a
        // WasAutoClosed session yesterday must NOT promote today's session to
        // overtime cap — it stays at the regular cap.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var nowUtc = DateTime.UtcNow;
        var shiftStart = TimeOnly.FromDateTime(nowUtc.AddHours(-1));
        var shiftEnd = shiftStart.AddHours(8);
        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: shiftStart,
            endTime: shiftEnd,
            maxOvertimeHours: 6,
            autoLogoutAfterHours: 2,
            autoLogoutRegularMinutes: 510); // 8.5h

        // Yesterday: auto-closed regular session.
        var yesterdayCheckIn = nowUtc.AddDays(-1).AddHours(-1);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, yesterdayCheckIn, yesterdayCheckIn.AddMinutes(510), wasAutoClosed: true);

        // Today: brand-new open session.
        var todayCheckIn = nowUtc.AddMinutes(-30);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, todayCheckIn, checkOutTime: null);

        var resp = await client.GetAsync("/api/work-sessions/current");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var logout = doc.RootElement.GetProperty("logoutAtUtc").GetDateTime();
        // Regular cap (510 min = 8.5h) from today's check-in, NOT 2h overtime.
        logout.Should().BeCloseTo(todayCheckIn.AddMinutes(510), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Current_keeps_overtime_cap_after_multiple_auto_closed_sessions_today()
    {
        // After 2 auto-closed sessions today, the 3rd is still an overtime
        // re-login → cap = AutoLogoutAfterHours (2h), not the regular cap.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var nowUtc = DateTime.UtcNow;
        var shiftStart = TimeOnly.FromDateTime(nowUtc.AddHours(-11));
        var shiftEnd = shiftStart.AddHours(8);
        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: shiftStart,
            endTime: shiftEnd,
            maxOvertimeHours: 6,
            autoLogoutAfterHours: 2,
            autoLogoutRegularMinutes: 510);

        // Two auto-closed sessions earlier today.
        var firstCheckIn = nowUtc.AddHours(-11);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, firstCheckIn, firstCheckIn.AddMinutes(510), wasAutoClosed: true);
        var secondCheckIn = nowUtc.AddHours(-2);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, secondCheckIn, secondCheckIn.AddHours(2), wasAutoClosed: true);

        // Third (re-login again).
        var thirdCheckIn = nowUtc.AddMinutes(-1);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, thirdCheckIn, checkOutTime: null);

        var resp = await client.GetAsync("/api/work-sessions/current");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var logout = doc.RootElement.GetProperty("logoutAtUtc").GetDateTime();
        logout.Should().BeCloseTo(thirdCheckIn.AddHours(2), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task AutoCheckout_notification_message_contains_worker_full_name()
    {
        // Bojan 30.05.2026: coordinator should know WHO was auto-logged-out.
        // The persisted Notification.Message must include the worker's full name.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory); // Admin
        var workerId = await TestDataSeeder.SeedAdditionalUserAsync(
            Factory, t.TenantId, UserRole.Department);

        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var checkIn = DateTime.UtcNow.AddMinutes(-30);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, workerId, checkIn, checkOutTime: null);

        var resp = await client.PostAsJsonAsync(
            "/api/work-sessions/auto-checkout", new { userId = workerId });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var notif = await db.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.TenantId == t.TenantId
                && n.Type == NotificationType.WorkerAutoLoggedOut
                && n.UserId == t.UserId)
            .SingleAsync();
        // TestDataSeeder seeds users as FirstName="Test", LastName="User" →
        // message should mention "Test User".
        notif.Message.Should().Contain("Test User");
    }

    [Fact]
    public async Task AutoCheckout_returns_error_when_no_active_session()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            "/api/work-sessions/auto-checkout", new { userId = t.UserId });
        // Existing CheckOut behaviour: throws DomainException("NOT_CHECKED_IN")
        // → mapped to 4xx by the global exception handler.
        resp.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }
}
