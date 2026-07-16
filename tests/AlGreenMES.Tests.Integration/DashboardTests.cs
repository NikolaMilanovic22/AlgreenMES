using System.Net;
using System.Text.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// /api/dashboard/* — first integration coverage for the manager dashboard
/// (DashboardQueryService). Fresh tenants auto-seed TenantSettings with the
/// 3-day critical / 7-day warning defaults, so orders bucket by their
/// delivery date: ≤3 days out = Critical, ≤7 = Warning, beyond = neither.
/// </summary>
public class DashboardTests : IntegrationTestBase
{
    public DashboardTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    private async Task SeedActiveOrderAsync(SeededTenant t, int deliveryDaysFromNow)
    {
        var orderId = await TestDataSeeder.SeedOrderAsync(
            Factory, t.TenantId, t.UserId,
            deliveryDate: DateTime.UtcNow.Date.AddDays(deliveryDaysFromNow));
        await TestDataSeeder.SetOrderStatusAsync(Factory, orderId, OrderStatus.Active);
    }

    [Fact]
    public async Task Statistics_countsCriticalAndWarningByDeliveryDate()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await SeedActiveOrderAsync(t, 2);   // Critical (≤3)
        await SeedActiveOrderAsync(t, 5);   // Warning (≤7, >3)
        await SeedActiveOrderAsync(t, 10);  // Neither (>7)

        var resp = await client.GetAsync("/api/dashboard/statistics");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var warnings = doc.RootElement.GetProperty("warnings");
        warnings.GetProperty("criticalCount").GetInt32().Should().Be(1);
        warnings.GetProperty("warningCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Warnings_returnsCriticalAndWarning_omitsBeyondThreshold()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var critical = await SeedActiveOrderReturningIdAsync(t, 2);
        var warning = await SeedActiveOrderReturningIdAsync(t, 5);
        var beyond = await SeedActiveOrderReturningIdAsync(t, 10);

        var resp = await client.GetAsync("/api/dashboard/warnings");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var rows = doc.RootElement.EnumerateArray()
            .ToDictionary(r => r.GetProperty("orderId").GetGuid(), r => r.GetProperty("level").GetString());

        rows.Should().ContainKey(critical);
        rows[critical].Should().Be("Critical");
        rows.Should().ContainKey(warning);
        rows[warning].Should().Be("Warning");
        rows.Should().NotContainKey(beyond, "orders beyond the warning window are not listed");
    }

    [Fact]
    public async Task Statistics_isIsolatedAcrossTenants()
    {
        // Tenant B has a critical active order; tenant A (empty) must see zero.
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
        await SeedActiveOrderAsync(b, 2);

        var clientA = await TestDataSeeder.AuthenticatedClientAsync(Factory, a);
        var resp = await clientA.GetAsync("/api/dashboard/statistics");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var warnings = doc.RootElement.GetProperty("warnings");
        warnings.GetProperty("criticalCount").GetInt32().Should().Be(0);
        warnings.GetProperty("warningCount").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("today").GetProperty("ordersActive").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task WorkersStatus_reportsCheckedIn_forDepartmentUserWithOpenSession()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var workerId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Department);
        // Open session (no check-out) stamped today.
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, workerId, checkInTime: DateTime.UtcNow, checkOutTime: null);

        var resp = await client.GetAsync("/api/dashboard/workers-status");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var worker = doc.RootElement.EnumerateArray()
            .Single(w => w.GetProperty("userId").GetGuid() == workerId);
        worker.GetProperty("isCheckedIn").GetBoolean().Should().BeTrue();
    }

    private async Task<Guid> SeedActiveOrderReturningIdAsync(SeededTenant t, int deliveryDaysFromNow)
    {
        var orderId = await TestDataSeeder.SeedOrderAsync(
            Factory, t.TenantId, t.UserId,
            deliveryDate: DateTime.UtcNow.Date.AddDays(deliveryDaysFromNow));
        await TestDataSeeder.SetOrderStatusAsync(Factory, orderId, OrderStatus.Active);
        return orderId;
    }
}
