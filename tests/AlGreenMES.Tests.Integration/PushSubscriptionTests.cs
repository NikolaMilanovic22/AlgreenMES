using System.Net;
using System.Net.Http.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// POST /api/push/subscribe security. The subscription owner must be the
/// AUTHENTICATED caller, never the client-supplied request.UserId — otherwise
/// any logged-in user could register their browser endpoint under another
/// user's id and then receive that user's push payloads (order/block content).
/// </summary>
public class PushSubscriptionTests : IntegrationTestBase
{
    public PushSubscriptionTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Subscribe_ignores_body_userId_and_stores_under_authenticated_caller()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var victimId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Department);

        var endpoint = "https://push.example/ep-" + Guid.NewGuid();
        var resp = await client.PostAsJsonAsync("/api/push/subscribe", new
        {
            userId = victimId, // attacker tries to subscribe AS the victim
            endpoint,
            p256dhKey = "p256",
            authKey = "auth",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var sub = await db.PushSubscriptions.IgnoreQueryFilters()
            .SingleAsync(s => s.Endpoint == endpoint);

        // Stored under the caller (from the token), NOT the victim from the body.
        sub.UserId.Should().Be(t.UserId);
        sub.UserId.Should().NotBe(victimId);
    }

    [Fact]
    public async Task Unsubscribe_ByDifferentUser_DoesNotKillTheOwnersSubscription()
    {
        // Only the owner may deactivate a subscription — a caller who learns
        // another user's endpoint must not be able to silently kill their push.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var ownerClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var (_, attackerEmail, attackerPassword) =
            await TestDataSeeder.SeedAdditionalUserWithCredsAsync(Factory, t.TenantId, UserRole.Department);

        var endpoint = "https://push.example/ep-" + Guid.NewGuid();
        (await ownerClient.PostAsJsonAsync("/api/push/subscribe", new
        {
            endpoint,
            p256dhKey = "p256",
            authKey = "auth",
        })).EnsureSuccessStatusCode();

        // Attacker (same tenant, different user) tries to unsubscribe it.
        var attackerClient = Factory.CreateClient();
        var attackerToken = await TestDataSeeder.LoginAndGetTokenAsync(
            attackerClient, attackerEmail, attackerPassword, t.TenantCode);
        attackerClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", attackerToken);
        var resp = await attackerClient.DeleteAsync(
            $"/api/push/unsubscribe?endpoint={Uri.EscapeDataString(endpoint)}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The owner's subscription is STILL active.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var sub = await db.PushSubscriptions.IgnoreQueryFilters()
            .SingleAsync(s => s.Endpoint == endpoint);
        sub.IsActive.Should().BeTrue();
        sub.UserId.Should().Be(t.UserId);
    }
}
