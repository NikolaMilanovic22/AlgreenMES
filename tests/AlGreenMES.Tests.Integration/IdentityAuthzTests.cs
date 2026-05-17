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
/// Sprint 3.0 backlog item F-6 — coverage for the role-management guards added
/// in Sprint 3.0 (F-1, F-2, F-3, F-7, F-11) plus the cross-tenant user-update
/// path. Each test names the finding it locks down so a future regression
/// points back at the right place in audit/02_findings.md.
/// </summary>
public class IdentityAuthzTests : IntegrationTestBase
{
    public IdentityAuthzTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    // Shared shape for PUT /api/users/{id}. Role-as-string matches the enum
    // converter on the controller (System.Text.Json JsonStringEnumConverter).
    private static object UpdateUserBody(string role, bool isActive = true) => new
    {
        firstName = "T",
        lastName = "User",
        role,
        isActive,
        canIncludeWithdrawnInAnalysis = false,
        processIds = (List<Guid>?)null,
    };

    // ---------------------------------------------------------------------
    // F-7 — Only SuperAdmin can change a user's role
    // ---------------------------------------------------------------------

    [Fact]
    public async Task UpdateUser_AdminChangesPeerAdminRole_Returns403_F7()
    {
        var admin1 = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var admin2Id = await TestDataSeeder.SeedAdditionalUserAsync(Factory, admin1.TenantId, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, admin1);

        var resp = await client.PutAsJsonAsync($"/api/users/{admin2Id}", UpdateUserBody("Manager"));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("FORBIDDEN_ROLE_CHANGE");
    }

    [Fact]
    public async Task UpdateUser_AdminDemotesSelf_Returns403_F7()
    {
        var admin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        // second admin so F-1 (last-admin) wouldn't fire first
        await TestDataSeeder.SeedAdditionalUserAsync(Factory, admin.TenantId, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, admin);

        var resp = await client.PutAsJsonAsync($"/api/users/{admin.UserId}", UpdateUserBody("Manager"));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("FORBIDDEN_ROLE_CHANGE");
    }

    [Fact]
    public async Task UpdateUser_AdminEditsOwnNameNotRole_Succeeds_F7Negative()
    {
        // F-7 only blocks role CHANGES. Editing non-role fields with the same
        // role value must still succeed for tenant Admins.
        var admin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        await TestDataSeeder.SeedAdditionalUserAsync(Factory, admin.TenantId, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, admin);

        var resp = await client.PutAsJsonAsync($"/api/users/{admin.UserId}", UpdateUserBody("Admin"));

        resp.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task UpdateUser_SuperAdminChangesRole_Succeeds_F7Negative()
    {
        var superAdmin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var target = await TestDataSeeder.SeedAdditionalUserAsync(Factory, superAdmin.TenantId, UserRole.Admin);
        // second admin so F-1 won't block the demotion
        await TestDataSeeder.SeedAdditionalUserAsync(Factory, superAdmin.TenantId, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, superAdmin);

        var resp = await client.PutAsJsonAsync($"/api/users/{target}", UpdateUserBody("Manager"));

        resp.EnsureSuccessStatusCode();
    }

    // ---------------------------------------------------------------------
    // F-1 — Last-Admin removal block (UpdateUser path)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task UpdateUser_SuperAdminDemotesLastAdmin_Returns400_F1()
    {
        var superAdmin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var lastAdminId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, superAdmin.TenantId, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, superAdmin);

        var resp = await client.PutAsJsonAsync($"/api/users/{lastAdminId}", UpdateUserBody("Manager"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("LAST_ADMIN_REMOVAL");
    }

    // ---------------------------------------------------------------------
    // F-2 — DeleteUser guards
    // ---------------------------------------------------------------------

    [Fact]
    public async Task DeleteUser_AdminDeletesSelf_Returns403_F2a()
    {
        var admin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        // second admin so F-2c (last-Admin) wouldn't fire first
        await TestDataSeeder.SeedAdditionalUserAsync(Factory, admin.TenantId, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, admin);

        var resp = await client.DeleteAsync($"/api/users/{admin.UserId}");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("SELF_DELETE_FORBIDDEN");
    }

    [Fact]
    public async Task DeleteUser_AdminDeletesSuperAdmin_Returns403_F2b()
    {
        var admin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        // a peer admin so the caller isn't the last admin themselves
        await TestDataSeeder.SeedAdditionalUserAsync(Factory, admin.TenantId, UserRole.Admin);
        var superAdminId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, admin.TenantId, UserRole.SuperAdmin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, admin);

        var resp = await client.DeleteAsync($"/api/users/{superAdminId}");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("FORBIDDEN_SUPERADMIN_DELETE");
    }

    [Fact]
    public async Task DeleteUser_SuperAdminDeletesLastAdmin_Returns400_F2c()
    {
        var superAdmin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var lastAdminId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, superAdmin.TenantId, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, superAdmin);

        var resp = await client.DeleteAsync($"/api/users/{lastAdminId}");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("LAST_ADMIN_REMOVAL");
    }

    // ---------------------------------------------------------------------
    // F-3 — Role change revokes refresh tokens
    // ---------------------------------------------------------------------

    [Fact]
    public async Task UpdateUser_RoleChange_RevokesTargetRefreshTokens_F3()
    {
        var superAdmin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var (targetId, targetEmail, targetPassword) =
            await TestDataSeeder.SeedAdditionalUserWithCredsAsync(Factory, superAdmin.TenantId, UserRole.Admin);
        // second admin so F-1 won't block the demotion
        await TestDataSeeder.SeedAdditionalUserAsync(Factory, superAdmin.TenantId, UserRole.Admin);

        // Target logs in → issues a refresh token
        var targetClient = Factory.CreateClient();
        await TestDataSeeder.LoginAndGetTokenAsync(targetClient, targetEmail, targetPassword, superAdmin.TenantCode);

        // SuperAdmin changes the target's role
        var superAdminClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, superAdmin);
        var resp = await superAdminClient.PutAsJsonAsync($"/api/users/{targetId}", UpdateUserBody("Manager"));
        resp.EnsureSuccessStatusCode();

        // All of the target's refresh tokens should be revoked.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var tokens = await db.RefreshTokens.IgnoreQueryFilters().AsNoTracking()
            .Where(rt => rt.UserId == targetId)
            .ToListAsync();
        tokens.Should().NotBeEmpty("the target had logged in and acquired at least one refresh token");
        tokens.Should().OnlyContain(rt => rt.IsRevoked,
            "F-3 requires every refresh token for the target to be revoked when the role changes");
    }

    // ---------------------------------------------------------------------
    // F-11 — ChangePassword is self-only (or SuperAdmin)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ChangePassword_AdminTargetsAnotherUser_Returns403_F11()
    {
        var admin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        await TestDataSeeder.SeedAdditionalUserAsync(Factory, admin.TenantId, UserRole.Admin);
        var (otherId, _, _) = await TestDataSeeder.SeedAdditionalUserWithCredsAsync(Factory, admin.TenantId, UserRole.Coordinator);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, admin);

        var resp = await client.PostAsJsonAsync($"/api/users/{otherId}/change-password", new
        {
            currentPassword = "whatever",
            newPassword = "NewPass123!",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("CHANGE_PASSWORD_NOT_SELF");
    }

    // ---------------------------------------------------------------------
    // Cross-tenant — even with valid tenant Admin creds, the target in
    // another tenant is invisible (HasQueryFilter → NotFound, not 403).
    // ---------------------------------------------------------------------

    [Fact]
    public async Task UpdateUser_AdminInTenantA_TargetsUserInTenantB_Returns404_CrossTenant()
    {
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory, UserRole.Admin, UserRole.Admin);
        await TestDataSeeder.SeedAdditionalUserAsync(Factory, a.TenantId, UserRole.Admin); // peer in A
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, a);

        var resp = await client.PutAsJsonAsync($"/api/users/{b.UserId}", UpdateUserBody("Manager"));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "tenant A must not be able to even see, let alone mutate, tenant B's users");
    }

    [Fact]
    public async Task DeleteUser_AdminInTenantA_TargetsUserInTenantB_Returns404_CrossTenant()
    {
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory, UserRole.Admin, UserRole.Admin);
        // peer admins so the caller's own tenant has 2 admins (no F-1 collision)
        await TestDataSeeder.SeedAdditionalUserAsync(Factory, a.TenantId, UserRole.Admin);
        await TestDataSeeder.SeedAdditionalUserAsync(Factory, b.TenantId, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, a);

        var resp = await client.DeleteAsync($"/api/users/{b.UserId}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Confirm the target wasn't deleted.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var stillExists = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(u => u.Id == b.UserId);
        stillExists.Should().BeTrue("tenant A's delete attempt must not have mutated tenant B");
    }
}
