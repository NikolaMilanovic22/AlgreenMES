using System.Net;
using System.Net.Http.Json;
using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// Coverage for the Sprint 3.x security hardening that landed 12.06.2026:
/// account lockout after N failed logins, the login_attempts audit table,
/// and refresh-token revocation on ChangePassword / ResetPassword.
/// </summary>
public class AuthSecurityTests : IntegrationTestBase
{
    public AuthSecurityTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    // ──────────────────────────────────────────────────────────────────────
    // Account lockout
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_AfterFiveFailedAttempts_LocksAccount()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);

        // 5 wrong-password attempts arm the lockout
        for (var i = 0; i < 5; i++)
        {
            var bad = await Client.PostAsJsonAsync("/api/auth/login", new
            {
                Email = t.Email,
                Password = "WrongPassword1!",
                TenantCode = t.TenantCode
            });
            bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // 6th attempt — even with the CORRECT password — is rejected as locked
        var correctButLocked = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = t.Email,
            Password = t.Password,
            TenantCode = t.TenantCode
        });
        correctButLocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await correctButLocked.Content.ReadFromJsonAsync<ErrorBody>();
        err!.Error.Code.Should().Be("ACCOUNT_LOCKED");

        // DB side: LockoutEnd is set, AccessFailedCount is at threshold
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == t.UserId);
        user.LockoutEnd.Should().NotBeNull();
        user.LockoutEnd!.Value.Should().BeAfter(DateTime.UtcNow);
        user.AccessFailedCount.Should().BeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public async Task Login_AccessFailedCountIncrementsPerWrongAttempt()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);

        // 3 wrong attempts — below threshold, so not yet locked
        for (var i = 0; i < 3; i++)
        {
            await Client.PostAsJsonAsync("/api/auth/login", new
            {
                Email = t.Email,
                Password = "WrongPass" + i + "!",
                TenantCode = t.TenantCode
            });
        }

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == t.UserId);
        user.AccessFailedCount.Should().Be(3);
        user.LockoutEnd.Should().BeNull();
    }

    [Fact]
    public async Task Login_SuccessfulLogin_ResetsFailedCount()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);

        // 2 wrong attempts
        for (var i = 0; i < 2; i++)
        {
            await Client.PostAsJsonAsync("/api/auth/login", new
            {
                Email = t.Email,
                Password = "WrongPass" + i + "!",
                TenantCode = t.TenantCode
            });
        }

        // Then the right one
        var ok = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = t.Email,
            Password = t.Password,
            TenantCode = t.TenantCode
        });
        ok.EnsureSuccessStatusCode();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == t.UserId);
        user.AccessFailedCount.Should().Be(0);
        user.LockoutEnd.Should().BeNull();
    }

    [Fact]
    public async Task Login_PasswordChangeClearsLockout()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);

        // Lock the account
        for (var i = 0; i < 5; i++)
        {
            await Client.PostAsJsonAsync("/api/auth/login", new
            {
                Email = t.Email,
                Password = "WrongPassword!",
                TenantCode = t.TenantCode
            });
        }

        // Admin (the SAME user is Admin in the seeded fixture) resets the
        // password via the in-domain ChangePassword method — exercises the
        // entity-level reset of AccessFailedCount + LockoutEnd that both
        // Change and Reset password handlers rely on.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == t.UserId);
            user.ChangePassword("new-hash-irrelevant-for-this-test");
            await db.SaveChangesAsync();

            user.AccessFailedCount.Should().Be(0);
            user.LockoutEnd.Should().BeNull();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // login_attempts audit log
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAttempt_Success_PersistsRowWithSucceededTrue()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);

        var resp = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = t.Email,
            Password = t.Password,
            TenantCode = t.TenantCode
        });
        resp.EnsureSuccessStatusCode();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var attempt = await db.LoginAttempts
            .Where(la => la.Email == t.Email.ToLowerInvariant())
            .OrderByDescending(la => la.AttemptedAt)
            .FirstAsync();

        attempt.Succeeded.Should().BeTrue();
        attempt.FailureReason.Should().BeNull();
        attempt.TenantId.Should().Be(t.TenantId);
    }

    [Fact]
    public async Task LoginAttempt_WrongPassword_PersistsRowWithReason()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);

        await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = t.Email,
            Password = "WrongPassword1!",
            TenantCode = t.TenantCode
        });

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var attempt = await db.LoginAttempts
            .Where(la => la.Email == t.Email.ToLowerInvariant())
            .OrderByDescending(la => la.AttemptedAt)
            .FirstAsync();

        attempt.Succeeded.Should().BeFalse();
        attempt.FailureReason.Should().Be("INVALID_CREDENTIALS");
        attempt.TenantId.Should().Be(t.TenantId);
    }

    [Fact]
    public async Task LoginAttempt_UnknownTenantCode_PersistsRowWithNullTenantId()
    {
        // Don't seed — we want the tenant lookup to fail. Use a random code.
        var bogusTenantCode = $"X{Guid.NewGuid():N}".Substring(0, 8).ToUpperInvariant();
        var probedEmail = $"probe-{Guid.NewGuid():N}@test.local";

        await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = probedEmail,
            Password = "AnyPassword1!",
            TenantCode = bogusTenantCode
        });

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var attempt = await db.LoginAttempts
            .Where(la => la.Email == probedEmail)
            .OrderByDescending(la => la.AttemptedAt)
            .FirstAsync();

        attempt.Succeeded.Should().BeFalse();
        attempt.FailureReason.Should().Be("TENANT_NOT_FOUND");
        attempt.TenantId.Should().BeNull("pre-auth attempts on unknown tenant codes must still log, with null tenant_id");
    }

    [Fact]
    public async Task LoginAttempt_AccountLocked_PersistsLockoutReason()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);

        // Lock the account
        for (var i = 0; i < 5; i++)
        {
            await Client.PostAsJsonAsync("/api/auth/login", new
            {
                Email = t.Email,
                Password = "WrongPassword!",
                TenantCode = t.TenantCode
            });
        }

        // One more attempt — should be denied as locked
        await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = t.Email,
            Password = "WrongPassword!",
            TenantCode = t.TenantCode
        });

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var lockoutAttempt = await db.LoginAttempts
            .Where(la => la.Email == t.Email.ToLowerInvariant() && la.FailureReason == "ACCOUNT_LOCKED")
            .OrderByDescending(la => la.AttemptedAt)
            .FirstAsync();

        lockoutAttempt.Succeeded.Should().BeFalse();
        lockoutAttempt.FailureReason.Should().Be("ACCOUNT_LOCKED");
    }

    // ──────────────────────────────────────────────────────────────────────
    // F-12 — refresh token revocation on password change/reset
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChangePassword_RevokesAllRefreshTokensForUser()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);

        // Issue a refresh token via login
        var loginResp = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = t.Email,
            Password = t.Password,
            TenantCode = t.TenantCode
        });
        loginResp.EnsureSuccessStatusCode();
        var loginBody = await loginResp.Content.ReadFromJsonAsync<LoginBody>();

        // Change password (call the authenticated endpoint with the token)
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody!.Token);
        var changeResp = await Client.PostAsJsonAsync($"/api/users/{t.UserId}/change-password", new
        {
            CurrentPassword = t.Password,
            NewPassword = "NewPass456!"
        });
        changeResp.EnsureSuccessStatusCode();
        Client.DefaultRequestHeaders.Authorization = null;

        // The refresh token that was issued before the password change must
        // no longer work — the BE should have revoked it.
        var refreshResp = await Client.PostAsJsonAsync("/api/auth/refresh", new
        {
            RefreshToken = loginBody.RefreshToken
        });
        refreshResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ResetPassword_RevokesAllRefreshTokensForTargetUser()
    {
        // Two users in the same tenant; admin resets the target's password
        var admin = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var targetEmail = $"target-{Guid.NewGuid():N}".Substring(0, 12) + "@test.local";
        Guid targetUserId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<AlGreenMES.Modules.Identity.Application.Services.IPasswordHasher>();
            var target = AlGreenMES.Modules.Identity.Domain.Entities.User.Create(
                admin.TenantId, targetEmail, hasher.HashPassword("TargetPass1!"),
                "Target", "User", AlGreenMES.Modules.Identity.Domain.Entities.UserRole.Department);
            db.Users.Add(target);
            await db.SaveChangesAsync();
            targetUserId = target.Id;
        }

        // Target logs in to get a refresh token
        var targetLogin = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = targetEmail,
            Password = "TargetPass1!",
            TenantCode = admin.TenantCode
        });
        targetLogin.EnsureSuccessStatusCode();
        var targetTokens = await targetLogin.Content.ReadFromJsonAsync<LoginBody>();

        // Admin signs in and resets the target user
        var adminToken = await TestDataSeeder.LoginAndGetTokenAsync(Client, admin.Email, admin.Password, admin.TenantCode);
        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
        var resetResp = await Client.PostAsJsonAsync($"/api/users/{targetUserId}/reset-password", new
        {
            NewPassword = "AdminSet123!"
        });
        resetResp.EnsureSuccessStatusCode();
        Client.DefaultRequestHeaders.Authorization = null;

        // Target's old refresh token is now revoked
        var refreshResp = await Client.PostAsJsonAsync("/api/auth/refresh", new
        {
            RefreshToken = targetTokens!.RefreshToken
        });
        refreshResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ──────────────────────────────────────────────────────────────────────
    // F-9 — user_role_change_log history table
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateUser_RoleChange_AppendsRoleChangeLog()
    {
        // SuperAdmin caller — needed to mutate roles (F-7).
        var superAdmin = await TestDataSeeder.SeedTenantWithUserAsync(
            Factory, role: AlGreenMES.Modules.Identity.Domain.Entities.UserRole.SuperAdmin);

        // Target user: a Coordinator we'll promote to Manager.
        var targetEmail = $"target-{Guid.NewGuid():N}".Substring(0, 12) + "@test.local";
        Guid targetUserId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<AlGreenMES.Modules.Identity.Application.Services.IPasswordHasher>();
            var target = AlGreenMES.Modules.Identity.Domain.Entities.User.Create(
                superAdmin.TenantId, targetEmail, hasher.HashPassword("TargetPass1!"),
                "Target", "User", AlGreenMES.Modules.Identity.Domain.Entities.UserRole.Coordinator);
            db.Users.Add(target);
            await db.SaveChangesAsync();
            targetUserId = target.Id;
        }

        var token = await TestDataSeeder.LoginAndGetTokenAsync(
            Client, superAdmin.Email, superAdmin.Password, superAdmin.TenantCode);
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await Client.PutAsJsonAsync($"/api/users/{targetUserId}", new
        {
            FirstName = "Target",
            LastName = "User",
            Role = "Manager",
            IsActive = true,
            CanIncludeWithdrawnInAnalysis = false,
            ProcessIds = (Guid[]?)null,
            AdditionalRoles = (string[]?)null,
        });
        Client.DefaultRequestHeaders.Authorization = null;
        resp.EnsureSuccessStatusCode();

        using var scope2 = Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var log = await db2.UserRoleChangeLogs
            .IgnoreQueryFilters()
            .Where(l => l.UserId == targetUserId)
            .OrderByDescending(l => l.ChangedAt)
            .FirstAsync();

        log.OldRole.Should().Be(AlGreenMES.Modules.Identity.Domain.Entities.UserRole.Coordinator);
        log.NewRole.Should().Be(AlGreenMES.Modules.Identity.Domain.Entities.UserRole.Manager);
        log.ChangedByUserId.Should().Be(superAdmin.UserId);
        log.TenantId.Should().Be(superAdmin.TenantId);
        log.ChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task UpdateUser_NoRoleChange_DoesNotAppendRoleChangeLog()
    {
        var superAdmin = await TestDataSeeder.SeedTenantWithUserAsync(
            Factory, role: AlGreenMES.Modules.Identity.Domain.Entities.UserRole.SuperAdmin);

        var token = await TestDataSeeder.LoginAndGetTokenAsync(
            Client, superAdmin.Email, superAdmin.Password, superAdmin.TenantCode);
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Update name only — same role
        var resp = await Client.PutAsJsonAsync($"/api/users/{superAdmin.UserId}", new
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
        resp.EnsureSuccessStatusCode();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var count = await db.UserRoleChangeLogs
            .IgnoreQueryFilters()
            .CountAsync(l => l.UserId == superAdmin.UserId);
        count.Should().Be(0, "non-role updates must not pollute the role-change history");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Admin endpoints reading audit tables (GET /users/{id}/login-history
    // and /role-history)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLoginHistory_ReturnsRecentAttemptsForUser()
    {
        var admin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, role: AlGreenMES.Modules.Identity.Domain.Entities.UserRole.Admin);

        // Generate a couple of attempts — 1 success, 2 failures
        await Client.PostAsJsonAsync("/api/auth/login", new { Email = admin.Email, Password = admin.Password, TenantCode = admin.TenantCode });
        await Client.PostAsJsonAsync("/api/auth/login", new { Email = admin.Email, Password = "WrongPass1!", TenantCode = admin.TenantCode });
        await Client.PostAsJsonAsync("/api/auth/login", new { Email = admin.Email, Password = "WrongPass2!", TenantCode = admin.TenantCode });

        var token = await TestDataSeeder.LoginAndGetTokenAsync(Client, admin.Email, admin.Password, admin.TenantCode);
        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await Client.GetAsync($"/api/users/{admin.UserId}/login-history?limit=10");
        Client.DefaultRequestHeaders.Authorization = null;
        resp.EnsureSuccessStatusCode();

        var entries = await resp.Content.ReadFromJsonAsync<List<LoginHistoryEntry>>();
        entries.Should().NotBeNullOrEmpty();
        entries!.All(e => e.Email == admin.Email.ToLowerInvariant()).Should().BeTrue();
        entries.Should().BeInDescendingOrder(e => e.AttemptedAt);
    }

    [Fact]
    public async Task GetLoginHistory_RespectsLimitCap()
    {
        var admin = await TestDataSeeder.SeedTenantWithUserAsync(Factory, role: AlGreenMES.Modules.Identity.Domain.Entities.UserRole.Admin);

        // Make 4 failed attempts so we'd return >3 rows without the cap.
        // Keep it under the 5-attempt lockout threshold so the subsequent
        // LoginAndGetTokenAsync still works.
        for (var i = 0; i < 4; i++)
        {
            await Client.PostAsJsonAsync("/api/auth/login", new { Email = admin.Email, Password = "Wrong" + i + "!", TenantCode = admin.TenantCode });
        }

        var token = await TestDataSeeder.LoginAndGetTokenAsync(Client, admin.Email, admin.Password, admin.TenantCode);
        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await Client.GetAsync($"/api/users/{admin.UserId}/login-history?limit=3");
        Client.DefaultRequestHeaders.Authorization = null;

        var entries = await resp.Content.ReadFromJsonAsync<List<LoginHistoryEntry>>();
        entries!.Count.Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public async Task GetRoleHistory_ReturnsEmptyForFreshUser()
    {
        var superAdmin = await TestDataSeeder.SeedTenantWithUserAsync(
            Factory, role: AlGreenMES.Modules.Identity.Domain.Entities.UserRole.SuperAdmin);

        var token = await TestDataSeeder.LoginAndGetTokenAsync(Client, superAdmin.Email, superAdmin.Password, superAdmin.TenantCode);
        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await Client.GetAsync($"/api/users/{superAdmin.UserId}/role-history");
        Client.DefaultRequestHeaders.Authorization = null;
        resp.EnsureSuccessStatusCode();

        var entries = await resp.Content.ReadFromJsonAsync<List<RoleHistoryEntry>>();
        entries.Should().NotBeNull();
        entries!.Should().BeEmpty("a newly-seeded user never had their role mutated");
    }

    private sealed record LoginBody(string Token, string RefreshToken);
    private sealed record ErrorBody(ErrorPayload Error);
    private sealed record ErrorPayload(string Code, string Message);
    private sealed record LoginHistoryEntry(Guid Id, string Email, string? IpAddress, string? UserAgent, bool Succeeded, string? FailureReason, DateTime AttemptedAt);
    private sealed record RoleHistoryEntry(Guid Id, string OldRole, string NewRole, Guid ChangedByUserId, string? ChangedByUserName, DateTime ChangedAt, string? Reason);
}
