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

    // ──────────────────────────────────────────────────────────────────────
    // Account-active + refresh-token lifecycle (deactivation, rotation, expiry)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_DeactivatedUser_IsRejectedWithUserInactive()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        await DeactivateUserAsync(t.UserId);

        var resp = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = t.Email,
            Password = t.Password,
            TenantCode = t.TenantCode
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await resp.Content.ReadFromJsonAsync<ErrorBody>();
        err!.Error.Code.Should().Be("USER_INACTIVE");
    }

    [Fact]
    public async Task Refresh_AfterUserDeactivated_IsRejected()
    {
        // A refresh token belonging to a now-inactive user must not keep minting
        // access tokens for the rest of the 7-day refresh TTL after the account
        // is disabled.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var login = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = t.Email,
            Password = t.Password,
            TenantCode = t.TenantCode
        });
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<LoginBody>();

        await DeactivateUserAsync(t.UserId);

        var refresh = await Client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = tokens!.RefreshToken });
        refresh.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await refresh.Content.ReadFromJsonAsync<ErrorBody>();
        err!.Error.Code.Should().Be("USER_INACTIVE");
    }

    [Fact]
    public async Task Refresh_RotatesToken_OriginalCannotBeReplayed()
    {
        // Rotation is the defense against stolen-refresh-token replay: a
        // successful refresh revokes the old token and issues a new pair.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var login = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = t.Email,
            Password = t.Password,
            TenantCode = t.TenantCode
        });
        login.EnsureSuccessStatusCode();
        var first = await login.Content.ReadFromJsonAsync<LoginBody>();

        var r1 = await Client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = first!.RefreshToken });
        r1.EnsureSuccessStatusCode();
        var second = await r1.Content.ReadFromJsonAsync<LoginBody>();

        // Replaying the ORIGINAL token is rejected (revoked on rotation)…
        var replay = await Client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = first.RefreshToken });
        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // …but the newly-issued token still works.
        var r2 = await Client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = second!.RefreshToken });
        r2.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Refresh_ExpiredToken_IsRejected()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var token = "expired-" + Guid.NewGuid().ToString("N");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.RefreshTokens.Add(AlGreenMES.Modules.Identity.Domain.Entities.RefreshToken.Create(
                t.TenantId, t.UserId, token, DateTime.UtcNow.AddDays(-1)));
            await db.SaveChangesAsync();
        }

        var refresh = await Client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = token });
        refresh.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await refresh.Content.ReadFromJsonAsync<ErrorBody>();
        err!.Error.Code.Should().Be("INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task Refresh_TokenWithMismatchedTenant_IsRejected()
    {
        // Defense-in-depth: for a non-SA user, a refresh token whose TenantId
        // differs from the user's home tenant is rejected.
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
        var token = "xtenant-" + Guid.NewGuid().ToString("N");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            // Token is for user A but carries tenant B's id.
            db.RefreshTokens.Add(AlGreenMES.Modules.Identity.Domain.Entities.RefreshToken.Create(
                b.TenantId, a.UserId, token, DateTime.UtcNow.AddDays(7)));
            await db.SaveChangesAsync();
        }

        var refresh = await Client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = token });
        refresh.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangePassword_SuperAdminTargetingAnotherUser_IsForbidden()
    {
        // Milos 16.06.2026 — change-password is self-only for EVERY role,
        // including SuperAdmin (the SA back door was removed). The endpoint
        // carries [AllowSuperAdminWrite] so the SA request reaches the handler;
        // the handler's self-only guard is the only thing stopping it.
        var sa = await TestDataSeeder.SeedTenantWithUserAsync(
            Factory, role: AlGreenMES.Modules.Identity.Domain.Entities.UserRole.SuperAdmin);
        var otherId = await TestDataSeeder.SeedAdditionalUserAsync(
            Factory, sa.TenantId, AlGreenMES.Modules.Identity.Domain.Entities.UserRole.Department);

        var token = await TestDataSeeder.LoginAndGetTokenAsync(Client, sa.Email, sa.Password, sa.TenantCode);
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var resp = await Client.PostAsJsonAsync($"/api/users/{otherId}/change-password", new
        {
            CurrentPassword = "irrelevant",
            NewPassword = "NewPass456!"
        });
        Client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var err = await resp.Content.ReadFromJsonAsync<ErrorBody>();
        err!.Error.Code.Should().Be("CHANGE_PASSWORD_NOT_SELF");
    }

    [Theory]
    [InlineData("12345678")]  // digit-only → no letter
    [InlineData("password")]  // letter-only → no digit
    [InlineData("Ab1")]       // too short (<8)
    public async Task ChangePassword_WeakNewPassword_IsRejected(string weak)
    {
        // BE PasswordRule must actually reject weak passwords on the write path
        // (the FE claims lockstep, but nothing pinned the server side).
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var token = await TestDataSeeder.LoginAndGetTokenAsync(Client, t.Email, t.Password, t.TenantCode);
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await Client.PostAsJsonAsync($"/api/users/{t.UserId}/change-password", new
        {
            CurrentPassword = t.Password, // correct, so we reach NewPassword validation
            NewPassword = weak
        });
        Client.DefaultRequestHeaders.Authorization = null;

        // FluentValidation failures surface as 422 (domain errors are 400).
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private async Task DeactivateUserAsync(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
    }

    private sealed record LoginBody(string Token, string RefreshToken);
    private sealed record ErrorBody(ErrorPayload Error);
    private sealed record ErrorPayload(string Code, string Message);
    private sealed record LoginHistoryEntry(Guid Id, string Email, string? IpAddress, string? UserAgent, bool Succeeded, string? FailureReason, DateTime AttemptedAt);
    private sealed record RoleHistoryEntry(Guid Id, string OldRole, string NewRole, Guid ChangedByUserId, string? ChangedByUserName, DateTime ChangedAt, string? Reason);
}
