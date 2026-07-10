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
    public async Task BlockedTenant_LocksOutRegularUser_ButSuperAdminCanStillLogIn()
    {
        // The SA-bypass in LoginCommandHandler (`user.Role != SuperAdmin`): a
        // blocked tenant locks out its regular users, but a SuperAdmin MUST
        // still be able to log in — otherwise nobody could ever unblock a
        // tenant that was blocked (a support lockout). Regular-user rejection
        // is covered above; this pins the bypass half.
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var target = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var saClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, sa);

        await saClient.PostAsJsonAsync($"/api/tenants/{target.TenantId}/block", new { Reason = "non-payment" });

        // Contrast: the blocked tenant's own Admin is rejected…
        var userResp = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = target.Email,
            Password = TestDataSeeder.DefaultPassword,
            TenantCode = target.TenantCode,
        });
        userResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // …but a SuperAdmin, even using the BLOCKED tenant's code, still gets in
        // so they can reach the platform to unblock it.
        var saLoginResp = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = sa.Email,
            Password = TestDataSeeder.DefaultPassword,
            TenantCode = target.TenantCode,
        });
        saLoginResp.StatusCode.Should().Be(HttpStatusCode.OK);
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
    public async Task BlockedTenant_StillAllowsSuperAdminLogin_ButRejectsRegularUser()
    {
        // Saša 17.06.2026: blocking the MPMS / platform tenant must NOT lock
        // SAs out — they need to be able to log in and unblock. Regular
        // users on the blocked tenant still see TENANT_BLOCKED.
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var target = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var saClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, sa);

        // Block the target tenant.
        var blockResp = await saClient.PostAsJsonAsync(
            $"/api/tenants/{target.TenantId}/block", new { Reason = "Saša test" });
        blockResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Regular Admin on the blocked tenant → TENANT_BLOCKED.
        var adminLogin = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = target.Email,
            Password = TestDataSeeder.DefaultPassword,
            TenantCode = target.TenantCode,
        });
        adminLogin.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await adminLogin.Content.ReadAsStringAsync()).Should().Contain("TENANT_BLOCKED");

        // SA logging in with the blocked tenant's code → succeeds.
        var saLogin = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = sa.Email,
            Password = TestDataSeeder.DefaultPassword,
            TenantCode = target.TenantCode,
        });
        saLogin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ──────────────────────────────────────────────────────────────────
    // Saša 18.06.2026 feedback batch — feature flags, paidThrough
    // semantics, aggregated payments endpoint.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NewTenant_DefaultsToBasicPlan_BothFeaturesDisabled()
    {
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var saClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, sa);

        var code = "TBP" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var createResp = await saClient.PostAsJsonAsync("/api/tenants", new
        {
            Name = "Test Basic Plan",
            Code = code,
            DefaultWarningDays = 7,
            DefaultCriticalDays = 3,
            WarningColor = "#FFA500",
            CriticalColor = "#FF0000",
        });
        createResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        var created = await createResp.Content.ReadFromJsonAsync<TenantWithFeaturesDto>();
        created!.DisabledFeatures.Should().BeEquivalentTo(new[] { "process-times", "magacin" });
    }

    [Fact]
    public async Task UpdateFeatures_SA_CanToggleFlagsAndPersist()
    {
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var target = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var saClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, sa);

        // Enable everything (clear the list).
        var enableAll = await saClient.PutAsJsonAsync(
            $"/api/tenants/{target.TenantId}/features",
            new { DisabledFeatures = Array.Empty<string>() });
        enableAll.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await (await saClient.GetAsync($"/api/tenants/{target.TenantId}")).Content
            .ReadFromJsonAsync<TenantWithFeaturesDto>();
        fetched!.DisabledFeatures.Should().BeEmpty();

        // Disable only magacin.
        var disableMagacin = await saClient.PutAsJsonAsync(
            $"/api/tenants/{target.TenantId}/features",
            new { DisabledFeatures = new[] { "magacin" } });
        disableMagacin.StatusCode.Should().Be(HttpStatusCode.OK);

        fetched = await (await saClient.GetAsync($"/api/tenants/{target.TenantId}")).Content
            .ReadFromJsonAsync<TenantWithFeaturesDto>();
        fetched!.DisabledFeatures.Should().BeEquivalentTo(new[] { "magacin" });
    }

    [Fact]
    public async Task UpdateFeatures_UnknownKey_Returns400()
    {
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var target = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var saClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, sa);

        var resp = await saClient.PutAsJsonAsync(
            $"/api/tenants/{target.TenantId}/features",
            new { DisabledFeatures = new[] { "magacin", "typo-feature-key" } });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("UNKNOWN_FEATURE");
    }

    [Fact]
    public async Task UpdateFeatures_RegularAdmin_GetsForbidden()
    {
        var target = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var adminClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, target);

        var resp = await adminClient.PutAsJsonAsync(
            $"/api/tenants/{target.TenantId}/features",
            new { DisabledFeatures = Array.Empty<string>() });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PaidThrough_OnlyCountsPaymentsWherePeriodHasStarted()
    {
        // Saša 18.06.2026: a payment with periodStart in the future doesn't
        // promote the tenant to "Plaćeno" until that date arrives. paidThrough
        // and lastPaidAt aggregates must filter on periodStart <= today.
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var target = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var saClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, sa);

        var futureStart = DateTime.UtcNow.Date.AddDays(30);
        var addResp = await saClient.PostAsJsonAsync(
            $"/api/tenants/{target.TenantId}/payments",
            new
            {
                PeriodStart = futureStart.ToString("yyyy-MM-dd"),
                PeriodEnd = futureStart.AddMonths(6).ToString("yyyy-MM-dd"),
                Amount = 100.00m,
                Currency = "EUR",
                PaidAt = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                InvoiceNumber = (string?)null,
                Notes = (string?)null,
            });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var tenant = await (await saClient.GetAsync($"/api/tenants/{target.TenantId}")).Content
            .ReadFromJsonAsync<TenantWithFeaturesDto>();
        tenant!.PaidThrough.Should().BeNull("payment's period hasn't started yet");
        tenant.LastPaidAt.Should().NotBeNull("paidAt itself was yesterday so the payment IS recorded");
    }

    [Fact]
    public async Task PaidThrough_CountsPaymentWhosePeriodHasAlreadyStarted()
    {
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var target = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var saClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, sa);

        var startedYesterday = DateTime.UtcNow.Date.AddDays(-1);
        var endsInFuture = startedYesterday.AddMonths(6);
        var addResp = await saClient.PostAsJsonAsync(
            $"/api/tenants/{target.TenantId}/payments",
            new
            {
                PeriodStart = startedYesterday.ToString("yyyy-MM-dd"),
                PeriodEnd = endsInFuture.ToString("yyyy-MM-dd"),
                Amount = 100.00m,
                Currency = "EUR",
                PaidAt = startedYesterday.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                InvoiceNumber = (string?)null,
                Notes = (string?)null,
            });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var tenant = await (await saClient.GetAsync($"/api/tenants/{target.TenantId}")).Content
            .ReadFromJsonAsync<TenantWithFeaturesDto>();
        tenant!.PaidThrough.Should().NotBeNull();
        tenant.PaidThrough!.Value.Date.Should().Be(endsInFuture);
    }

    [Fact]
    public async Task GetAllPayments_ReturnsRowsFromMultipleTenants_WithTenantNameAndCode()
    {
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
        var saClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, sa);

        await AddSimplePaymentAsync(saClient, a.TenantId, amount: 100m, paidAt: DateTime.UtcNow.Date.AddDays(-2));
        await AddSimplePaymentAsync(saClient, b.TenantId, amount: 200m, paidAt: DateTime.UtcNow.Date.AddDays(-1));

        var resp = await saClient.GetAsync("/api/tenants/payments?page=1&pageSize=50");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain(a.TenantCode);
        body.Should().Contain(b.TenantCode);
        body.Should().Contain("100");
        body.Should().Contain("200");
    }

    [Fact]
    public async Task GetAllPayments_FilteredByTenantId_ReturnsOnlyThatTenantsRows()
    {
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
        var saClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, sa);

        await AddSimplePaymentAsync(saClient, a.TenantId, amount: 100m, paidAt: DateTime.UtcNow.Date.AddDays(-2));
        await AddSimplePaymentAsync(saClient, b.TenantId, amount: 200m, paidAt: DateTime.UtcNow.Date.AddDays(-1));

        var resp = await saClient.GetAsync($"/api/tenants/payments?tenantId={a.TenantId}&page=1&pageSize=50");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain(a.TenantCode);
        body.Should().NotContain(b.TenantCode);
    }

    [Fact]
    public async Task GetAllPayments_RegularAdmin_GetsForbidden()
    {
        var target = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var adminClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, target);

        var resp = await adminClient.GetAsync("/api/tenants/payments");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task AddSimplePaymentAsync(HttpClient saClient, Guid tenantId, decimal amount, DateTime paidAt)
    {
        var resp = await saClient.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/payments",
            new
            {
                PeriodStart = paidAt.ToString("yyyy-MM-dd"),
                PeriodEnd = paidAt.AddMonths(1).ToString("yyyy-MM-dd"),
                Amount = amount,
                Currency = "EUR",
                PaidAt = paidAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                InvoiceNumber = (string?)null,
                Notes = (string?)null,
            });
        resp.EnsureSuccessStatusCode();
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

    // Mirror of the BE TenantDto fields the tests need. Letting the test
    // own a local shape keeps it independent of internal DTO churn — only
    // breaks if these specific fields change semantics.
    private sealed record TenantWithFeaturesDto(
        Guid Id,
        string Name,
        string Code,
        bool IsActive,
        DateTime CreatedAt,
        DateTime? UpdatedAt,
        string? LogoUrl,
        DateTime? BlockedAt,
        string? BlockedReason,
        DateTime? LastPaidAt,
        DateTime? PaidThrough,
        List<string>? DisabledFeatures);
}
