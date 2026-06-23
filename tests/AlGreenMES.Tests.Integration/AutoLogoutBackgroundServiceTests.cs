using System.Reflection;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using AlGreenMES.Tests.Integration.Helpers;
using AlgreenMES.API.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// AutoLogoutBackgroundService scans every 2 minutes for open work sessions
/// across all tenants, builds a synthetic HTTP context per session, and
/// invokes GetActiveWorkSessionQuery — whose lazy safety net fires
/// AutoCheckOutCommand when the session is past its cap.
///
/// The query handler itself is well-covered in ActiveWorkSessionTests via
/// HTTP. What's NOT covered is the BG service's own enumeration + per-
/// session synthetic-context construction. That's the most likely silent-
/// failure point — if the tenant_id claim key changes, or the
/// IgnoreQueryFilters() drops away, or the projection-to-ValueTuple bug
/// from Bojan 03.06.2026 regresses, the service will catch its own
/// exceptions and log warnings but never close any session. Workers rack
/// up unbounded hours, payroll corrupts, discovered weeks later via
/// admin-dashboard spot-check.
///
/// These tests pin down the scan loop end-to-end against the real DB.
/// </summary>
public class AutoLogoutBackgroundServiceTests : IntegrationTestBase
{
    public AutoLogoutBackgroundServiceTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task ScanAsync_AutoClosesOpenSessionPastCap_AndMarksWasAutoClosed()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);

        // 1h cap from a shift that's brackets UtcNow → the safety net inside
        // GetActiveWorkSessionQuery sees logoutAt 30 min ago and fires AutoCheckOut.
        var nowUtc = DateTime.UtcNow;
        var shiftStart = TimeOnly.FromDateTime(nowUtc.AddHours(-2));
        var shiftEnd = shiftStart.AddHours(8);
        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: shiftStart,
            endTime: shiftEnd,
            autoLogoutRegularMinutes: 60);

        var checkIn = nowUtc.AddMinutes(-90); // 30 min past cap
        var sessionId = await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, checkIn, checkOutTime: null);

        var processed = await InvokeScanAsync();

        processed.Should().BeGreaterThanOrEqualTo(1, "the open session must be enumerated and processed");
        var session = await GetSessionAsync(sessionId);
        session.WasAutoClosed.Should().BeTrue("scan must mark the session as auto-closed end-to-end");
        session.CheckOutTime.Should().NotBeNull("scan must close the session");
        session.CheckOutTime!.Value.Should().BeCloseTo(checkIn.AddMinutes(60), TimeSpan.FromSeconds(2),
            "checkOut must be backdated to the cap moment, not when the scan ran — otherwise reports count phantom hours past the cap");
    }

    [Fact]
    public async Task ScanAsync_DoesNotCloseSessionStillWithinCap()
    {
        // A still-active session (well within its cap) must be left alone. A
        // regression that backdates every open session would be catastrophic.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);

        var nowUtc = DateTime.UtcNow;
        var shiftStart = TimeOnly.FromDateTime(nowUtc.AddHours(-1));
        var shiftEnd = shiftStart.AddHours(8);
        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: shiftStart,
            endTime: shiftEnd,
            autoLogoutRegularMinutes: 480); // 8h cap

        var checkIn = nowUtc.AddMinutes(-30); // 30 min in → 7.5h left
        var sessionId = await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, checkIn, checkOutTime: null);

        await InvokeScanAsync();

        var session = await GetSessionAsync(sessionId);
        session.CheckOutTime.Should().BeNull("a session still within its cap must remain open after a scan tick");
        session.WasAutoClosed.Should().BeFalse();
    }

    [Fact]
    public async Task ScanAsync_ProcessesSessionsAcrossMultipleTenantsIndependently()
    {
        // Bojan 03.06.2026: the bootstrap scope enumerates open sessions
        // across ALL tenants (IgnoreQueryFilters). The per-session scope
        // then builds a synthetic context PER tenant — a regression that
        // hardcodes the first session's tenant for everyone would silently
        // misroute the close to the wrong tenant, or skip everyone but the
        // first.
        var tenantA = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var tenantB = await TestDataSeeder.SeedTenantWithUserAsync(Factory);

        var nowUtc = DateTime.UtcNow;
        var shiftStart = TimeOnly.FromDateTime(nowUtc.AddHours(-2));
        var shiftEnd = shiftStart.AddHours(8);
        foreach (var t in new[] { tenantA, tenantB })
        {
            await TestDataSeeder.SeedShiftAsync(
                Factory, t.TenantId,
                startTime: shiftStart,
                endTime: shiftEnd,
                autoLogoutRegularMinutes: 60);
        }

        var checkIn = nowUtc.AddMinutes(-90);
        var sessionA = await TestDataSeeder.SeedWorkSessionAsync(
            Factory, tenantA.TenantId, tenantA.UserId, checkIn, checkOutTime: null);
        var sessionB = await TestDataSeeder.SeedWorkSessionAsync(
            Factory, tenantB.TenantId, tenantB.UserId, checkIn, checkOutTime: null);

        await InvokeScanAsync();

        var a = await GetSessionAsync(sessionA);
        var b = await GetSessionAsync(sessionB);
        a.WasAutoClosed.Should().BeTrue("tenant A's expired session must be closed");
        b.WasAutoClosed.Should().BeTrue("tenant B's expired session must be closed independently");
    }

    private async Task<int> InvokeScanAsync()
    {
        // ScanAsync is private; reflection lets the test pin the actual
        // production scan loop (synthetic HttpContext + per-tenant scope
        // construction) rather than re-implementing it. The production
        // code stays untouched.
        var service = new AutoLogoutBackgroundService(
            Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AutoLogoutBackgroundService>.Instance);
        var method = typeof(AutoLogoutBackgroundService).GetMethod(
            "ScanAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task<int>)method.Invoke(service, new object[] { CancellationToken.None })!;
        return await task;
    }

    private async Task<AlGreenMES.Modules.Orders.Domain.Entities.WorkSession> GetSessionAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await ordersDb.WorkSessions
            .IgnoreQueryFilters()
            .SingleAsync(ws => ws.Id == id);
    }
}
