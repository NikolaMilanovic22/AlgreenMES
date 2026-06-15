using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// Coverage for the SuperAdmin cross-tenant login flow (15.06.2026):
/// - Logging in with the target tenant's code while the user's HOME tenant
///   is different succeeds ONLY when the user is a SuperAdmin.
/// - The resulting JWT carries cross_tenant_session=true + home_tenant_id.
/// - The CrossTenantReadOnlyMiddleware rejects all non-GET requests with
///   403 READ_ONLY_CROSS_TENANT — every existing and future write endpoint
///   is implicitly covered (we sample with /api/tenants/me/settings PUT).
/// - GET requests pass through (the SA can browse the foreign tenant).
/// - Non-SuperAdmins attempting a cross-tenant login collapse to
///   INVALID_CREDENTIALS to avoid leaking user existence across tenants.
/// </summary>
public class CrossTenantSuperAdminTests : IntegrationTestBase
{
    public CrossTenantSuperAdminTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Login_SuperAdmin_IntoForeignTenantCode_Succeeds_WithCrossTenantClaim()
    {
        var superAdmin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var foreign = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var resp = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = superAdmin.Email,
            Password = superAdmin.Password,
            TenantCode = foreign.TenantCode,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<LoginBody>();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body!.Token);

        jwt.Claims.Should().Contain(c => c.Type == "cross_tenant_session" && c.Value == "true");
        jwt.Claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == foreign.TenantId.ToString());
        jwt.Claims.Should().Contain(c => c.Type == "home_tenant_id" && c.Value == superAdmin.TenantId.ToString());
    }

    [Fact]
    public async Task Login_SuperAdmin_IntoHomeTenant_DoesNotMarkCrossTenant()
    {
        var superAdmin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);

        var resp = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = superAdmin.Email,
            Password = superAdmin.Password,
            TenantCode = superAdmin.TenantCode,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<LoginBody>();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body!.Token);
        jwt.Claims.Should().NotContain(c => c.Type == "cross_tenant_session");
        jwt.Claims.Should().NotContain(c => c.Type == "home_tenant_id");
    }

    [Fact]
    public async Task Login_NonSuperAdmin_IntoForeignTenantCode_CollapsesToInvalidCredentials()
    {
        // Coordinator in A trying to log into B's tenant code: the user
        // doesn't exist in B, and cross-tenant fallback is rejected since
        // they're not SuperAdmin. Surface should be the same as "wrong
        // password" so we don't leak that the email exists somewhere.
        var coord = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Coordinator);
        var foreign = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var resp = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = coord.Email,
            Password = coord.Password,
            TenantCode = foreign.TenantCode,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await resp.Content.ReadFromJsonAsync<ErrorBody>();
        err!.Error.Code.Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task CrossTenantSession_NonGetRequest_Returns403ReadOnly()
    {
        var superAdmin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var foreign = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var token = await TestDataSeeder.LoginAndGetTokenAsync(
            Client, superAdmin.Email, superAdmin.Password, foreign.TenantCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // PUT against a real write endpoint — middleware blocks before
        // routing reaches the handler.
        var resp = await Client.PutAsJsonAsync("/api/tenants/me/settings", new
        {
            DefaultWarningDays = 5,
            DefaultCriticalDays = 2,
            WarningColor = "#FFA500",
            CriticalColor = "#FF0000",
        });
        Client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var err = await resp.Content.ReadFromJsonAsync<ErrorBody>();
        err!.Error.Code.Should().Be("READ_ONLY_CROSS_TENANT");
    }

    [Fact]
    public async Task CrossTenantSession_GetRequest_PassesThrough()
    {
        var superAdmin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var foreign = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var token = await TestDataSeeder.LoginAndGetTokenAsync(
            Client, superAdmin.Email, superAdmin.Password, foreign.TenantCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await Client.GetAsync("/api/tenants/me");
        Client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CrossTenantSession_GetMeReturns_TargetTenant_NotHome()
    {
        // The /me endpoints resolve tenant from the JWT's tenant_id claim,
        // which on a cross-tenant session is the TARGET tenant. So the SA
        // sees the foreign tenant's data, not their own home.
        var superAdmin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var foreign = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var token = await TestDataSeeder.LoginAndGetTokenAsync(
            Client, superAdmin.Email, superAdmin.Password, foreign.TenantCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await Client.GetAsync("/api/tenants/me");
        Client.DefaultRequestHeaders.Authorization = null;
        resp.EnsureSuccessStatusCode();

        var tenant = await resp.Content.ReadFromJsonAsync<TenantBody>();
        tenant!.Id.Should().Be(foreign.TenantId);
        tenant.Code.Should().Be(foreign.TenantCode);
    }

    [Fact]
    public async Task RefreshToken_FromCrossTenantSession_RemainsCrossTenant()
    {
        // The refresh handler accepts a cross-tenant refresh only for
        // SuperAdmins and reissues a cross-tenant token — without this
        // the SA's foreign-tenant session would silently downgrade on the
        // first refresh (15-min mark) into a home-tenant session, which
        // would be a privilege escalation surprise.
        var superAdmin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var foreign = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var login = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = superAdmin.Email,
            Password = superAdmin.Password,
            TenantCode = foreign.TenantCode,
        });
        login.EnsureSuccessStatusCode();
        var loginBody = await login.Content.ReadFromJsonAsync<LoginBody>();

        var refresh = await Client.PostAsJsonAsync("/api/auth/refresh", new
        {
            RefreshToken = loginBody!.RefreshToken,
        });
        refresh.EnsureSuccessStatusCode();
        var refreshBody = await refresh.Content.ReadFromJsonAsync<LoginBody>();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(refreshBody!.Token);

        jwt.Claims.Should().Contain(c => c.Type == "cross_tenant_session" && c.Value == "true");
        jwt.Claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == foreign.TenantId.ToString());
    }

    private sealed record LoginBody(string Token, string RefreshToken);
    private sealed record ErrorBody(ErrorPayload Error);
    private sealed record ErrorPayload(string Code, string Message);
    private sealed record TenantBody(Guid Id, string Name, string Code, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt, string? LogoUrl);
}
