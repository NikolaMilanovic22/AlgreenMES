using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration.CrossTenant;

public class NotificationsCrossTenantTests : IntegrationTestBase
{
    public NotificationsCrossTenantTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    private sealed record NotifRow(Guid Id, bool IsRead);
    private sealed record NotifPage(IReadOnlyList<NotifRow> Items, int TotalCount);

    private async Task<HttpClient> ClientForUserAsync(string email, string password, string tenantCode)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TestDataSeeder.LoginAndGetTokenAsync(client, email, password, tenantCode));
        return client;
    }

    [Fact]
    public async Task BulkMarkRead_and_DeleteAll_are_scoped_to_the_calling_user()
    {
        // One tenant, two users. A1's read-all + delete-all must not touch A2's
        // rows (NotificationRepository scopes both by userId).
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var (a2Id, a2Email, a2Pw) = await TestDataSeeder.SeedAdditionalUserWithCredsAsync(Factory, t.TenantId);

        await TestDataSeeder.SeedNotificationAsync(Factory, t.TenantId, t.UserId, "A1-1");
        await TestDataSeeder.SeedNotificationAsync(Factory, t.TenantId, t.UserId, "A1-2");
        await TestDataSeeder.SeedNotificationAsync(Factory, t.TenantId, a2Id, "A2-1");
        await TestDataSeeder.SeedNotificationAsync(Factory, t.TenantId, a2Id, "A2-2");

        var clientA1 = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        (await clientA1.PostAsync("/api/notifications/read-all", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await clientA1.DeleteAsync("/api/notifications")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // A1's list is now empty.
        var a1Page = (await clientA1.GetFromJsonAsync<NotifPage>("/api/notifications?pageSize=50"))!;
        a1Page.Items.Should().BeEmpty();

        // A2 still has both rows, both unread.
        var clientA2 = await ClientForUserAsync(a2Email, a2Pw, t.TenantCode);
        var a2Page = (await clientA2.GetFromJsonAsync<NotifPage>("/api/notifications?pageSize=50"))!;
        a2Page.Items.Should().HaveCount(2);
        a2Page.Items.Should().OnlyContain(n => !n.IsRead, "A1's bulk actions must not affect A2");
    }

    [Fact]
    public async Task SingleMarkRead_and_Delete_of_another_users_notification_returns_404_and_leaves_it_intact()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var (a2Id, a2Email, a2Pw) = await TestDataSeeder.SeedAdditionalUserWithCredsAsync(Factory, t.TenantId);
        var a2NotifId = await TestDataSeeder.SeedNotificationAsync(Factory, t.TenantId, a2Id, "A2-ONLY");

        var clientA1 = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        // A1 cannot mark A2's notification read (handler 404s on user mismatch).
        (await clientA1.PostAsync($"/api/notifications/{a2NotifId}/read", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Nor delete it.
        (await clientA1.DeleteAsync($"/api/notifications/{a2NotifId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // A2's row is untouched: present and still unread.
        var clientA2 = await ClientForUserAsync(a2Email, a2Pw, t.TenantCode);
        var a2Page = (await clientA2.GetFromJsonAsync<NotifPage>("/api/notifications?pageSize=50"))!;
        var row = a2Page.Items.Single(n => n.Id == a2NotifId);
        row.IsRead.Should().BeFalse();
    }

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
