using System.Net;
using System.Net.Http.Json;
using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AlGreenMES.Tests.Integration;

public class AuthTests : IntegrationTestBase
{
    public AuthTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsToken()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);

        var resp = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = t.Email,
            Password = t.Password,
            TenantCode = t.TenantCode
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<LoginBody>();
        body!.Token.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_WithInvalidPassword_Returns400_InvalidCredentials()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);

        var resp = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = t.Email,
            Password = "WrongPassword!",
            TenantCode = t.TenantCode
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await resp.Content.ReadFromJsonAsync<ErrorBody>();
        err!.Error.Code.Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Login_WithNonexistentUser_Returns400_InvalidCredentials()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);

        var resp = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = $"missing-{Guid.NewGuid():N}@test.local",
            Password = "AnyPassword!",
            TenantCode = t.TenantCode
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await resp.Content.ReadFromJsonAsync<ErrorBody>();
        err!.Error.Code.Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task RefreshToken_AfterUserDeleted_Returns400_AndCascadeRemovesToken()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);

        var loginResp = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = t.Email,
            Password = t.Password,
            TenantCode = t.TenantCode
        });
        loginResp.EnsureSuccessStatusCode();
        var loginBody = await loginResp.Content.ReadFromJsonAsync<LoginBody>();
        loginBody!.RefreshToken.Should().NotBeNullOrEmpty();

        // Hard-delete the user via direct DbContext (simulates admin removal).
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == t.UserId);
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }

        // Cascade FK should have purged the refresh token row alongside the user.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var stillExists = await db.RefreshTokens.IgnoreQueryFilters()
                .AnyAsync(rt => rt.Token == loginBody.RefreshToken);
            stillExists.Should().BeFalse("FK cascade must remove the refresh token when the user is deleted");
        }

        // Refresh now returns INVALID_REFRESH_TOKEN (400) — the token row is gone.
        var refreshResp = await Client.PostAsJsonAsync("/api/auth/refresh", new
        {
            RefreshToken = loginBody.RefreshToken
        });
        refreshResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await refreshResp.Content.ReadFromJsonAsync<ErrorBody>();
        err!.Error.Code.Should().Be("INVALID_REFRESH_TOKEN");
    }

    private sealed record LoginBody(string Token, string RefreshToken);
    private sealed record ErrorBody(ErrorPayload Error);
    private sealed record ErrorPayload(string Code, string Message);
}
