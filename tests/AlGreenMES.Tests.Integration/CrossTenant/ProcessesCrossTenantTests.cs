using System.Net;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration.CrossTenant;

public class ProcessesCrossTenantTests : IntegrationTestBase
{
    public ProcessesCrossTenantTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task GetProcessById_FromOtherTenant_Returns404()
    {
        var (tenantA, tenantB) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
        var processInB = await TestDataSeeder.SeedProcessAsync(Factory, tenantB.TenantId, tenantB.UserId);
        var clientA = await TestDataSeeder.AuthenticatedClientAsync(Factory, tenantA);

        var response = await clientA.GetAsync($"/api/processes/{processInB}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "tenant A must not be able to see tenant B's processes by ID");
    }

    [Fact]
    public async Task GetProcesses_DoesNotReturnOtherTenantProcesses()
    {
        // Sprint 2.4b removed the ?tenantId= query param; the param being gone means
        // even an attempted ?tenantId=B is silently ignored — tenant scope comes from JWT.
        var (tenantA, tenantB) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
        await TestDataSeeder.SeedProcessAsync(Factory, tenantA.TenantId, tenantA.UserId, processName: "Proc-A-MARKER");
        await TestDataSeeder.SeedProcessAsync(Factory, tenantB.TenantId, tenantB.UserId, processName: "Proc-B-MARKER");
        var clientA = await TestDataSeeder.AuthenticatedClientAsync(Factory, tenantA);

        var response = await clientA.GetAsync($"/api/processes?tenantId={tenantB.TenantId}");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("Proc-B-MARKER",
            "tenant A must not see tenant B's processes even if a foreign tenantId is appended to the query");
    }
}
