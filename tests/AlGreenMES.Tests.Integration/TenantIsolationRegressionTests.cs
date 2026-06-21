using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// Defence against the 19.06.2026 regression where the EF
/// HasQueryFilter lambda silently no-op'd after TenantEntity.TenantId
/// became nullable — every TenantEntity child was returning cross-tenant
/// rows for 3 days before integration tests caught it.
///
/// One golden-path test per high-risk listing endpoint: seed two
/// tenants, write entities in tenant A, log in as tenant B's Admin,
/// confirm tenant A's data is NOT visible. If any of these light up
/// red, the tenant filter has regressed somewhere — fix at the
/// DbContext/HasQueryFilter layer, not by patching individual endpoints.
/// </summary>
public class TenantIsolationRegressionTests : IntegrationTestBase
{
    public TenantIsolationRegressionTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task GetOrders_AsTenantB_DoesNotLeakTenantAOrders()
    {
        var tenantA = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var tenantB = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var tenantAOrderId = await TestDataSeeder.SeedOrderAsync(Factory, tenantA.TenantId, tenantA.UserId);

        var clientB = await TestDataSeeder.AuthenticatedClientAsync(Factory, tenantB);
        var resp = await clientB.GetAsync("/api/orders?pageSize=100");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotContain(tenantAOrderId.ToString(),
            "tenant B's Admin must not see tenant A's orders in the listing");
    }

    [Fact]
    public async Task GetProcesses_AsTenantB_DoesNotLeakTenantAProcesses()
    {
        var tenantA = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var tenantB = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var tenantAProcessId = await TestDataSeeder.SeedProcessAsync(Factory, tenantA.TenantId, tenantA.UserId);

        var clientB = await TestDataSeeder.AuthenticatedClientAsync(Factory, tenantB);
        var resp = await clientB.GetAsync("/api/processes");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotContain(tenantAProcessId.ToString(),
            "tenant B's Admin must not see tenant A's processes in the listing");
    }

    [Fact]
    public async Task GetShifts_AsTenantB_DoesNotLeakTenantAShifts()
    {
        var tenantA = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var tenantB = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var tenantAShiftId = await TestDataSeeder.SeedShiftAsync(Factory, tenantA.TenantId);

        var clientB = await TestDataSeeder.AuthenticatedClientAsync(Factory, tenantB);
        var resp = await clientB.GetAsync("/api/shifts");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotContain(tenantAShiftId.ToString(),
            "tenant B's Admin must not see tenant A's shifts in the listing");
    }

    [Fact]
    public async Task GetUsers_AsTenantB_DoesNotLeakTenantAUsers()
    {
        var tenantA = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var tenantB = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var clientB = await TestDataSeeder.AuthenticatedClientAsync(Factory, tenantB);
        var resp = await clientB.GetAsync("/api/users?pageSize=100");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotContain(tenantA.UserId.ToString(),
            "tenant B's Admin must not see tenant A's users in the listing");
    }
}
