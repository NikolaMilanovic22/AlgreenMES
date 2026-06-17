using System.Net;
using System.Net.Http.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// Smoke coverage for the SA-only Naplata feature (Milos 16.06.2026).
///
/// Invariants:
/// - Only SuperAdmins can hit the payment + block/unblock endpoints.
/// - Block() flips Tenant.IsActive to false AND populates BlockedAt; the
///   login flow distinguishes TENANT_BLOCKED from generic TENANT_INACTIVE
///   so the FE can show the right copy.
/// - Unblock() flips IsActive back to true and clears BlockedAt + reason.
/// - Payments are tenant-scoped: DELETE on a foreign tenant's payment id
///   returns 404.
/// </summary>
public class TenantBillingTests : IntegrationTestBase
{
    public TenantBillingTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task BlockTenant_FlipsIsActiveAndLoginReturnsTenantBlocked()
    {
        // ── arrange: SA + a target tenant Admin
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var target = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var saClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, sa);

        // ── act: block the target tenant
        var blockResp = await saClient.PostAsJsonAsync(
            $"/api/tenants/{target.TenantId}/block",
            new { Reason = "Unpaid for Q1" });

        blockResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // ── assert: target tenant's Admin can no longer log in
        var loginResp = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = target.Email,
            Password = TestDataSeeder.DefaultPassword,
            TenantCode = target.TenantCode,
        });

        loginResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await loginResp.Content.ReadAsStringAsync();
        body.Should().Contain("TENANT_BLOCKED");
    }

    [Fact]
    public async Task UnblockTenant_RestoresLoginAndClearsReason()
    {
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var target = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var saClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, sa);

        await saClient.PostAsJsonAsync($"/api/tenants/{target.TenantId}/block", new { Reason = "test" });

        var unblockResp = await saClient.PostAsJsonAsync($"/api/tenants/{target.TenantId}/unblock", new { });
        unblockResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Login flows again
        var loginResp = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = target.Email,
            Password = TestDataSeeder.DefaultPassword,
            TenantCode = target.TenantCode,
        });
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AddPayment_RoundTripsAndAppearsInList()
    {
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var target = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var saClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, sa);

        var addResp = await saClient.PostAsJsonAsync(
            $"/api/tenants/{target.TenantId}/payments",
            new
            {
                PeriodStart = "2026-01-01",
                PeriodEnd = "2026-03-31",
                Amount = 150.00m,
                Currency = "EUR",
                PaidAt = "2026-01-05T00:00:00Z",
                InvoiceNumber = "INV-2026-Q1",
                Notes = (string?)null,
            });

        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var listResp = await saClient.GetAsync($"/api/tenants/{target.TenantId}/payments");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await listResp.Content.ReadAsStringAsync();
        body.Should().Contain("INV-2026-Q1");
        body.Should().Contain("EUR");
    }

    [Fact]
    public async Task RegularAdmin_CannotHitBillingEndpoints()
    {
        var target = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var adminClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, target);

        var listResp = await adminClient.GetAsync($"/api/tenants/{target.TenantId}/payments");
        listResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var blockResp = await adminClient.PostAsJsonAsync(
            $"/api/tenants/{target.TenantId}/block", new { Reason = "self-block?" });
        blockResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdatePayment_OverwritesFieldsAndPersistsAcrossList()
    {
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var target = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var saClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, sa);

        var addResp = await saClient.PostAsJsonAsync(
            $"/api/tenants/{target.TenantId}/payments",
            new
            {
                PeriodStart = "2026-01-01",
                PeriodEnd = "2026-03-31",
                Amount = 100.00m,
                Currency = "EUR",
                PaidAt = "2026-01-05T00:00:00Z",
                InvoiceNumber = "INV-OLD",
                Notes = (string?)null,
            });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createdId = (await addResp.Content.ReadFromJsonAsync<TenantPaymentRow>())!.Id;

        var updateResp = await saClient.PutAsJsonAsync(
            $"/api/tenants/{target.TenantId}/payments/{createdId}",
            new
            {
                PeriodStart = "2026-04-01",
                PeriodEnd = "2026-06-30",
                Amount = 200.00m,
                Currency = "USD",
                PaidAt = "2026-04-10T00:00:00Z",
                InvoiceNumber = "INV-NEW",
                Notes = "moved to Q2",
            });
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var listBody = await (await saClient.GetAsync($"/api/tenants/{target.TenantId}/payments")).Content.ReadAsStringAsync();
        listBody.Should().Contain("INV-NEW").And.Contain("USD").And.Contain("moved to Q2");
        listBody.Should().NotContain("INV-OLD");
    }

    [Fact]
    public async Task UpdatePayment_FromForeignTenant_Returns404()
    {
        // Path-tampered request: payment belongs to tenant A but the SA
        // calls the endpoint under tenant B's id. Handler verifies
        // payment.TenantId == request.TenantId before applying changes.
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
        var saClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, sa);

        var addResp = await saClient.PostAsJsonAsync(
            $"/api/tenants/{a.TenantId}/payments",
            new
            {
                PeriodStart = "2026-01-01",
                PeriodEnd = "2026-03-31",
                Amount = 50.00m,
                Currency = "EUR",
                PaidAt = "2026-01-05T00:00:00Z",
                InvoiceNumber = (string?)null,
                Notes = (string?)null,
            });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var paymentId = (await addResp.Content.ReadFromJsonAsync<TenantPaymentRow>())!.Id;

        var updateResp = await saClient.PutAsJsonAsync(
            $"/api/tenants/{b.TenantId}/payments/{paymentId}",
            new
            {
                PeriodStart = "2026-04-01",
                PeriodEnd = "2026-06-30",
                Amount = 1.00m,
                Currency = "EUR",
                PaidAt = "2026-04-10T00:00:00Z",
                InvoiceNumber = (string?)null,
                Notes = (string?)null,
            });
        updateResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SA_HittingNonExistentRoute_Gets404_NotSuperAdminReadOnly()
    {
        // Regression for a misleading 403: the middleware used to fire
        // SUPERADMIN_READ_ONLY for any non-GET SA request that didn't
        // resolve to an MVC action. A typo'd or removed URL would look
        // like an authorisation error instead of a 404 — which made it
        // hard to tell a stale BE binary apart from a missing
        // [AllowSuperAdminWrite] attribute. Now: no matched action →
        // pipeline continues → MVC returns the proper 404.
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var saClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, sa);

        var resp = await saClient.PostAsJsonAsync("/api/this-route-does-not-exist-anywhere", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotContain("SUPERADMIN_READ_ONLY");
    }

    private sealed record TenantPaymentRow(Guid Id);
}
