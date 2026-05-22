using System.Net;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration.CrossTenant;

public class NotificationsCrossTenantTests : IntegrationTestBase
{
    public NotificationsCrossTenantTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    // After Sprint 2.4c, NotificationsController derives userId from the JWT sub claim
    // and ignores any client-supplied ?userId=. Defense in depth is layered:
    //   1. HasQueryFilter (2.4a) hides foreign-tenant data at the DB level.
    //   2. The userId query param no longer exists on the endpoint (2.4c).
    //   3. Single-id mutate handlers verify notification.UserId == currentUserId and 404 on mismatch.
    [Fact]
    public async Task GetNotifications_DoesNotReturnOtherTenantNotifications()
    {
        var (tenantA, tenantB) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
        await TestDataSeeder.SeedNotificationAsync(Factory, tenantB.TenantId, tenantB.UserId, title: "B-NOTIF-MARKER");
        var clientA = await TestDataSeeder.AuthenticatedClientAsync(Factory, tenantA);

        // Plain request — tenant scope from JWT, B's notification invisible.
        var plain = await clientA.GetAsync("/api/notifications");
        plain.StatusCode.Should().Be(HttpStatusCode.OK);
        (await plain.Content.ReadAsStringAsync()).Should().NotContain("B-NOTIF-MARKER",
            "tenant scope is enforced via HasQueryFilter and JWT claim");

        // Stray ?userId=tenantB.UserId is now unbound and silently ignored.
        var withStrayParam = await clientA.GetAsync($"/api/notifications?userId={tenantB.UserId}");
        withStrayParam.StatusCode.Should().Be(HttpStatusCode.OK);
        (await withStrayParam.Content.ReadAsStringAsync()).Should().NotContain("B-NOTIF-MARKER",
            "stray userId param must not influence the result after 2.4c");
    }

    [Fact]
    public async Task GetNotifications_DoesNotReturnOtherUsersNotificationsInSameTenant()
    {
        // User A1 and User A2 both in tenant A — A1 must not see A2's notifications.
        var tenantA1 = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var userA2Id = await TestDataSeeder.SeedAdditionalUserAsync(Factory, tenantA1.TenantId);
        await TestDataSeeder.SeedNotificationAsync(Factory, tenantA1.TenantId, userA2Id, title: "A2-NOTIF-MARKER");
        var clientA1 = await TestDataSeeder.AuthenticatedClientAsync(Factory, tenantA1);

        var response = await clientA1.GetAsync("/api/notifications");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("A2-NOTIF-MARKER",
            "user A1 must not see user A2's notifications even within the same tenant");
    }
}
