using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// Coverage for the tenantless SuperAdmin model (Milos 16.06.2026).
///
/// Key invariants:
/// - SuperAdmin rows carry tenant_id = NULL. They never show up in
///   tenant-scoped listings.
/// - Any tenant code at login is accepted for an SA; the JWT carries that
///   tenant id as the scope for reads.
/// - The JWT does NOT carry the old cross_tenant_session / home_tenant_id
///   claims — that machinery is gone.
/// - SuperAdminReadOnlyMiddleware blocks all non-GET requests for SA
///   callers UNLESS the matched action carries [AllowSuperAdminWrite]
///   (tenant CRUD, SA creation, own password change). Error code
///   SUPERADMIN_READ_ONLY.
/// - Non-SuperAdmin cross-tenant logins collapse to INVALID_CREDENTIALS
///   so we don't leak email existence across tenants.
/// </summary>
public class TenantlessSuperAdminTests : IntegrationTestBase
{
    public TenantlessSuperAdminTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task SuperAdminUser_PersistsWith_NullTenantId()
    {
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var row = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == sa.UserId);
        row.TenantId.Should().BeNull();
        row.Role.Should().Be(UserRole.SuperAdmin);
    }

    [Fact]
    public async Task Login_SuperAdmin_WithAnyTenantCode_Succeeds_NoCrossTenantClaim()
    {
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var anotherTenant = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var resp = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = sa.Email,
            Password = sa.Password,
            TenantCode = anotherTenant.TenantCode,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<LoginBody>();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body!.Token);

        jwt.Claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == anotherTenant.TenantId.ToString());
        jwt.Claims.Should().NotContain(c => c.Type == "cross_tenant_session");
        jwt.Claims.Should().NotContain(c => c.Type == "home_tenant_id");
    }

    [Fact]
    public async Task Login_NonSuperAdmin_IntoForeignTenantCode_CollapsesToInvalidCredentials()
    {
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
    public async Task SuperAdmin_NonGetWrite_AgainstNonAllowlistedEndpoint_Returns403()
    {
        // /api/tenants/me/settings PUT is NOT on the SA allow-list — it's a
        // tenant Admin write. SA hitting it must be blocked by the
        // SuperAdminReadOnly middleware.
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var foreign = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var token = await TestDataSeeder.LoginAndGetTokenAsync(
            Client, sa.Email, sa.Password, foreign.TenantCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

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
        err!.Error.Code.Should().Be("SUPERADMIN_READ_ONLY");
    }

    [Fact]
    public async Task SuperAdmin_GetRequest_PassesThrough()
    {
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var foreign = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var token = await TestDataSeeder.LoginAndGetTokenAsync(
            Client, sa.Email, sa.Password, foreign.TenantCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await Client.GetAsync("/api/tenants/me");
        Client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SuperAdmin_GetMe_Returns_TargetTenant_NotSomeOther()
    {
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var foreign = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var token = await TestDataSeeder.LoginAndGetTokenAsync(
            Client, sa.Email, sa.Password, foreign.TenantCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await Client.GetAsync("/api/tenants/me");
        Client.DefaultRequestHeaders.Authorization = null;
        resp.EnsureSuccessStatusCode();

        var tenant = await resp.Content.ReadFromJsonAsync<TenantBody>();
        tenant!.Id.Should().Be(foreign.TenantId);
        tenant.Code.Should().Be(foreign.TenantCode);
    }

    [Fact]
    public async Task SuperAdmin_Write_ToAllowlistedEndpoint_Succeeds()
    {
        // Tenant management is on the allow-list ([AllowSuperAdminWrite]
        // on TenantsController). SA must be able to POST tenants.
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);

        var token = await TestDataSeeder.LoginAndGetTokenAsync(
            Client, sa.Email, sa.Password, sa.TenantCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var code = $"T{Guid.NewGuid():N}".Substring(0, 8).ToUpperInvariant();
        var resp = await Client.PostAsJsonAsync("/api/tenants", new
        {
            Name = $"Tenant {code}",
            Code = code,
        });
        Client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task RefreshToken_FromSuperAdminSession_KeepsScopedToSameTenant()
    {
        // The refresh handler regenerates the JWT with the tenant the
        // refresh token was originally issued for. For an SA browsing
        // foreign.TenantCode, the refreshed JWT must keep
        // tenant_id = foreign.TenantId — otherwise the session would
        // silently jump to some other tenant on the first refresh.
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var foreign = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var login = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = sa.Email,
            Password = sa.Password,
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

        jwt.Claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == foreign.TenantId.ToString());
        jwt.Claims.Should().NotContain(c => c.Type == "cross_tenant_session");
    }

    private sealed record LoginBody(string Token, string RefreshToken);
    private sealed record ErrorBody(ErrorPayload Error);
    private sealed record ErrorPayload(string Code, string Message);
    private sealed record TenantBody(Guid Id, string Name, string Code, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt, string? LogoUrl);
}
