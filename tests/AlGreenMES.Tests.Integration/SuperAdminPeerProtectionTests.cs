using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// Peer SuperAdmin protection (15.06.2026): SuperAdmins can additively
/// create new SuperAdmins, but cannot edit / delete / reset-password
/// another SuperAdmin. Self-modification IS allowed (a SA can rename
/// themselves) — only peer-targeting operations are blocked. Combined
/// with the existing "no role transitions across SuperAdmin boundary"
/// rule, this means SuperAdmin accounts are immutable from the outside
/// once seeded.
///
/// Also covers:
/// - GetSuperAdmins endpoint is SuperAdmin-only.
/// - Regular GET /api/users excludes SuperAdmin rows so tenant Admins
///   never see platform-level accounts.
/// </summary>
public class SuperAdminPeerProtectionTests : IntegrationTestBase
{
    public SuperAdminPeerProtectionTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    // ──────────────────────────────────────────────────────────────────────
    // Peer protection: write paths
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateUser_AnotherSuperAdmin_ReturnsForbiddenPeer()
    {
        var caller = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var peerId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, caller.TenantId, UserRole.SuperAdmin);

        var token = await TestDataSeeder.LoginAndGetTokenAsync(Client, caller.Email, caller.Password, caller.TenantCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await Client.PutAsJsonAsync($"/api/users/{peerId}", new
        {
            FirstName = "Hijacked",
            LastName = "User",
            Role = "SuperAdmin",
            IsActive = false,
            CanIncludeWithdrawnInAnalysis = false,
            ProcessIds = (Guid[]?)null,
            AdditionalRoles = (string[]?)null,
        });
        Client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var err = await resp.Content.ReadFromJsonAsync<ErrorBody>();
        err!.Error.Code.Should().Be("FORBIDDEN_PEER_SUPERADMIN");
    }

    [Fact]
    public async Task UpdateUser_Self_AsSuperAdmin_Succeeds()
    {
        // Per class doc: self-modification IS allowed; only peer-targeting
        // operations against another SuperAdmin are blocked. A SA can rename
        // / toggle active on their own row through /api/users PUT — the
        // peer-SA handler guard exempts the self-target case (Milos
        // 15.06.2026).
        var caller = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);

        var token = await TestDataSeeder.LoginAndGetTokenAsync(Client, caller.Email, caller.Password, caller.TenantCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await Client.PutAsJsonAsync($"/api/users/{caller.UserId}", new
        {
            FirstName = "Renamed",
            LastName = "User",
            Role = "SuperAdmin",
            IsActive = true,
            CanIncludeWithdrawnInAnalysis = false,
            ProcessIds = (Guid[]?)null,
            AdditionalRoles = (string[]?)null,
        });
        Client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteUser_AnotherSuperAdmin_ReturnsForbiddenPeer()
    {
        var caller = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var peerId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, caller.TenantId, UserRole.SuperAdmin);

        var token = await TestDataSeeder.LoginAndGetTokenAsync(Client, caller.Email, caller.Password, caller.TenantCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await Client.DeleteAsync($"/api/users/{peerId}");
        Client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var err = await resp.Content.ReadFromJsonAsync<ErrorBody>();
        err!.Error.Code.Should().Be("FORBIDDEN_PEER_SUPERADMIN");
    }

    [Fact]
    public async Task ResetPassword_AnotherSuperAdmin_ReturnsForbiddenPeer()
    {
        var caller = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var peerId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, caller.TenantId, UserRole.SuperAdmin);

        var token = await TestDataSeeder.LoginAndGetTokenAsync(Client, caller.Email, caller.Password, caller.TenantCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await Client.PostAsJsonAsync($"/api/users/{peerId}/reset-password", new
        {
            NewPassword = "Hijack123!",
        });
        Client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var err = await resp.Content.ReadFromJsonAsync<ErrorBody>();
        err!.Error.Code.Should().Be("FORBIDDEN_PEER_SUPERADMIN");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Create — additive escalation allowed only for SAs
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateUser_AsSuperAdmin_WithRoleSuperAdmin_Succeeds()
    {
        var caller = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);

        var token = await TestDataSeeder.LoginAndGetTokenAsync(Client, caller.Email, caller.Password, caller.TenantCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await Client.PostAsJsonAsync("/api/users", new
        {
            Email = $"new-sa-{Guid.NewGuid():N}@test.local",
            Password = "NewSaPass123!",
            FirstName = "New",
            LastName = "Sa",
            Role = "SuperAdmin",
            TenantId = (Guid?)null,
            ProcessIds = (Guid[]?)null,
        });
        Client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateUser_AsAdmin_WithRoleSuperAdmin_ReturnsForbidden()
    {
        // Sprint 3.0 F-7 hardening — only SuperAdmin can grant SuperAdmin.
        // A tenant Admin attempting it gets FORBIDDEN_ROLE_ASSIGNMENT.
        var caller = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var token = await TestDataSeeder.LoginAndGetTokenAsync(Client, caller.Email, caller.Password, caller.TenantCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await Client.PostAsJsonAsync("/api/users", new
        {
            Email = $"sneak-sa-{Guid.NewGuid():N}@test.local",
            Password = "SneakPass1!",
            FirstName = "Sneaky",
            LastName = "Admin",
            Role = "SuperAdmin",
            TenantId = (Guid?)null,
            ProcessIds = (Guid[]?)null,
        });
        Client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var err = await resp.Content.ReadFromJsonAsync<ErrorBody>();
        err!.Error.Code.Should().Be("FORBIDDEN_ROLE_ASSIGNMENT");
    }

    // ──────────────────────────────────────────────────────────────────────
    // GetSuperAdmins endpoint
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSuperAdmins_AsAdmin_Returns403()
    {
        var caller = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, caller);

        var resp = await client.GetAsync("/api/users/super-admins");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetSuperAdmins_AsSuperAdmin_ListsAllSuperAdmins_AcrossTenants()
    {
        // SuperAdmin in tenant A + another SuperAdmin in tenant B — the
        // listing must surface BOTH (cross-tenant).
        var saA = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var tenantBSeed = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var saB_id = await TestDataSeeder.SeedAdditionalUserAsync(Factory, tenantBSeed.TenantId, UserRole.SuperAdmin);

        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, saA);
        var resp = await client.GetAsync("/api/users/super-admins");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var users = await resp.Content.ReadFromJsonAsync<List<UserListEntry>>();
        users.Should().NotBeNull();
        users!.Select(u => u.Id).Should().Contain(saA.UserId);
        users!.Select(u => u.Id).Should().Contain(saB_id);
        users!.All(u => u.Role == "SuperAdmin").Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────
    // SuperAdmin invisibility on the regular GET /api/users listing
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUsers_DoesNotInclude_SuperAdmins()
    {
        // Tenant Admin sees only non-SA users in their listing — the
        // listing endpoint silently filters out platform-level accounts so
        // their existence isn't leaked.
        var admin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var hiddenSaId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, admin.TenantId, UserRole.SuperAdmin);

        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, admin);
        var resp = await client.GetAsync("/api/users?pageSize=100");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await resp.Content.ReadFromJsonAsync<PagedUsers>();
        page.Should().NotBeNull();
        page!.Items.Select(u => u.Id).Should().NotContain(hiddenSaId);
        page.Items.All(u => u.Role != "SuperAdmin").Should().BeTrue();
    }

    [Fact]
    public async Task GetUsers_AsSuperAdmin_AlsoDoesNotIncludeSuperAdmins()
    {
        // Even a SuperAdmin viewing the regular listing should NOT see
        // other SAs — the SuperAdmin tab is the canonical place for that.
        // Keeps a single source of truth and prevents accidental peer
        // actions from a confused row.
        var caller = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.SuperAdmin);
        var hiddenSaId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, caller.TenantId, UserRole.SuperAdmin);

        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, caller);
        var resp = await client.GetAsync("/api/users?pageSize=100");
        var page = await resp.Content.ReadFromJsonAsync<PagedUsers>();
        page!.Items!.Select(u => u.Id).Should().NotContain(hiddenSaId);
        // The caller themselves is a SuperAdmin; they shouldn't appear in
        // the regular listing either.
        page.Items.Select(u => u.Id).Should().NotContain(caller.UserId);
    }

    private sealed record ErrorBody(ErrorPayload Error);
    private sealed record ErrorPayload(string Code, string Message);
    private sealed record UserListEntry(Guid Id, string Email, string FirstName, string LastName, string Role, bool IsActive, Guid TenantId);
    private sealed record PagedUsers(List<UserListEntry> Items, int TotalCount, int Page, int PageSize);
}
