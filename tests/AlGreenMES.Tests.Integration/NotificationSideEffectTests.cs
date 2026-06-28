using System.Net;
using System.Net.Http.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// Workflow handlers fire IProductionEventService notifications as a side
/// effect (Notification rows + SignalR broadcast). The state-transition
/// happy paths covered in WorkflowTests assert only the primary entity
/// changed — they do NOT check that the bell-feeding Notification rows
/// landed for the right recipients. That's the silent-failure class of
/// bug Saša-style users discover via "I didn't get a notification" — the
/// state change worked but downstream recipients never saw it because of
/// a typo in role-filter or recipient-list logic.
///
/// These tests pin down the recipient lists by role for the highest-bite
/// notification paths. SignalR broadcasts are NOT asserted here (would
/// require a SignalR test client); the Notification rows are the
/// persistent side-effect that drives the bell badge and matter most.
/// </summary>
public class NotificationSideEffectTests : IntegrationTestBase
{
    public NotificationSideEffectTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task ActivateOrder_CreatesNotificationForEveryActiveUserInTenant()
    {
        // NotifyOrderActivatedAsync uses CreateNotificationsForAllUsersAsync —
        // "ALL active users" (workers + dashboard + sales). A role-filter
        // typo here would silently lose workers from the bell badge.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var workerId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Department);
        var managerId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Manager);
        var (orderId, _, _) = await SeedDraftOrderWithItemAsync(t);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsync($"/api/orders/{orderId}/activate", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var recipientIds = await GetNotificationRecipientsAsync(t.TenantId, NotificationType.OrderActivated);
        recipientIds.Should().Contain(new[] { t.UserId, workerId, managerId },
            "OrderActivated fans out to every active user — workers must see the bell to know a new order needs them");
    }

    [Fact]
    public async Task BlockProcess_CreatesProcessBlockedNotificationForDashboardUsersOnly()
    {
        // NotifyProcessBlockedAsync uses CreateNotificationsForDashboardUsersAsync —
        // restricted to Admin/Manager/Coordinator/SalesManager. Department
        // (worker) role must NOT receive this — they already see the blockade
        // on their tablet and don't have a dashboard bell.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var workerId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Department);
        var coordinatorId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Coordinator);
        var (_, oipId) = await SeedActiveOrderInProgressAsync(t);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/block",
            new { userId = t.UserId, reason = "test" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var recipientIds = await GetNotificationRecipientsAsync(t.TenantId, NotificationType.ProcessBlocked);
        recipientIds.Should().Contain(new[] { t.UserId, coordinatorId },
            "dashboard roles need the bell so a coordinator can intervene");
        recipientIds.Should().NotContain(workerId,
            "workers see blockades on the tablet directly — sending them dashboard bells would be noise");
    }

    [Fact]
    public async Task CreateBlockRequest_FiresBlockRequestNotificationForDashboardUsers()
    {
        // The most-bitten silent-failure class: worker raises a block
        // request, it shows in the list but no bell fires — coordinator
        // misses it for hours.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var coordinatorId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Coordinator);
        var workerId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Department);
        var (_, oipId) = await SeedActiveOrderInProgressAsync(t);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync("/api/block-requests", new
        {
            orderItemProcessId = oipId,
            orderItemSubProcessId = (Guid?)null,
            requestedByUserId = workerId,
            requestNote = "machine broken",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var recipientIds = await GetNotificationRecipientsAsync(t.TenantId, NotificationType.BlockRequest);
        recipientIds.Should().Contain(new[] { t.UserId, coordinatorId });
        recipientIds.Should().NotContain(workerId,
            "the requesting worker doesn't need a self-notification; they're the one who clicked the button");
    }

    [Fact]
    public async Task ApproveBlockRequest_NotifiesBothDashboardUsersAndTheRequestingWorker()
    {
        // Dual-recipient path: dashboard roles get a "decided" bell, AND
        // the worker who raised the request gets a "your request was
        // approved" bell. A bug in either branch leaves one side silent.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var coordinatorId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Coordinator);
        var workerId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Department);
        var (_, oipId) = await SeedActiveOrderInProgressAsync(t);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var create = await client.PostAsJsonAsync("/api/block-requests", new
        {
            orderItemProcessId = oipId,
            orderItemSubProcessId = (Guid?)null,
            requestedByUserId = workerId,
            requestNote = "needs supervisor decision",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var brId = await GetMostRecentBlockRequestIdAsync(t.TenantId);

        var approve = await client.PostAsJsonAsync(
            $"/api/block-requests/{brId}/approve",
            new { handledByUserId = t.UserId, note = "ok" });
        approve.StatusCode.Should().Be(HttpStatusCode.OK);

        var recipientIds = await GetNotificationRecipientsAsync(t.TenantId, NotificationType.BlockRequestApproved);
        recipientIds.Should().Contain(workerId, "the worker who raised it must learn the outcome");
        recipientIds.Should().Contain(new[] { t.UserId, coordinatorId }, "dashboard users need an audit-trail bell");
    }

    [Fact]
    public async Task RejectBlockRequest_NotifiesOnlyTheRequestingWorker()
    {
        // Opposite mirror of approve: ONLY the requesting worker is told.
        // A broadcast-to-everyone bug here is a privacy / noise problem —
        // every dashboard role would be told "this random worker's request
        // got rejected" which clutters the bell.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var coordinatorId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Coordinator);
        var workerId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Department);
        var (_, oipId) = await SeedActiveOrderInProgressAsync(t);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var create = await client.PostAsJsonAsync("/api/block-requests", new
        {
            orderItemProcessId = oipId,
            orderItemSubProcessId = (Guid?)null,
            requestedByUserId = workerId,
            requestNote = "test",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var brId = await GetMostRecentBlockRequestIdAsync(t.TenantId);

        var reject = await client.PostAsJsonAsync(
            $"/api/block-requests/{brId}/reject",
            new { handledByUserId = t.UserId, note = "machine works fine, false alarm" });
        reject.StatusCode.Should().Be(HttpStatusCode.OK);

        var recipientIds = await GetNotificationRecipientsAsync(t.TenantId, NotificationType.BlockRequestRejected);
        recipientIds.Should().BeEquivalentTo(new[] { workerId },
            "rejection is a personal message to the requester — nobody else should see it in their bell");
    }

    [Fact]
    public async Task CreateChangeRequest_FiresChangeRequestNotificationForDashboardUsers()
    {
        // Mirror of CreateBlockRequest but on the order-modification path.
        // Same silent-failure class.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var coordinatorId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Coordinator);
        var workerId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Department);
        var (orderId, _, _) = await SeedDraftOrderWithItemAsync(t);
        await TestDataSeeder.SetOrderStatusAsync(Factory, orderId, OrderStatus.Active);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync("/api/change-requests", new
        {
            orderId,
            requestedByUserId = t.UserId,
            requestType = "Modify",
            description = "change quantity",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var recipientIds = await GetNotificationRecipientsAsync(t.TenantId, NotificationType.ChangeRequest);
        recipientIds.Should().Contain(new[] { t.UserId, coordinatorId });
        recipientIds.Should().NotContain(workerId);
    }

    [Fact]
    public async Task UnreadCount_CapsAt100_WhenUserHasMoreUnread()
    {
        // GET /api/Notifications/unread-count is polled every 60s by every
        // logged-in user. The handler counts unread rows; without a cap, the
        // query cost grows linearly per user as notifications accumulate
        // (Sentry weekly 27.06.2026 caught the trend — alblue staging
        // tenant had 300+ unread per manager/coord).
        //
        // The FE bell badge (antd <Badge> default overflowCount=99) already
        // truncates display to "99+" for any value > 99, so the BE never
        // needs to return the true count past 100. This test pins the cap
        // so a future refactor can't quietly drop it.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        // Seed 150 unread notifications for the admin. Use the existing
        // helper one row at a time — it's the simplest path; 150 inserts
        // are still well under a second.
        for (int i = 0; i < 150; i++)
        {
            await TestDataSeeder.SeedNotificationAsync(Factory, t.TenantId, t.UserId, title: $"n-{i}");
        }

        var resp = await client.GetAsync("/api/Notifications/unread-count");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        var count = int.Parse(body);
        count.Should().Be(100, "cap is 100 — past that, every increment is wasted work because the FE only shows '99+'");
    }

    // ---------------------------------------------------------------------
    // Shared helpers — keep test bodies focused on the assertion. Mirrors
    // the pattern in WorkflowTests.cs.
    // ---------------------------------------------------------------------

    private async Task<(Guid OrderId, Guid ItemId, Guid OipId)> SeedDraftOrderWithItemAsync(SeededTenant t)
    {
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var (orderId, itemId, oips) = await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, t.TenantId, t.UserId, categoryId,
            processIds: new[] { processId },
            processStatuses: new[] { ProcessStatus.Pending });
        return (orderId, itemId, oips[0]);
    }

    private async Task<(SeededTenant Tenant, Guid OipId)> SeedActiveOrderInProgressAsync(SeededTenant t)
    {
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.InProgress);
        using (var scope = Factory.Services.CreateScope())
        {
            var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var orderId = await ordersDb.OrderItemProcesses
                .IgnoreQueryFilters()
                .Where(p => p.Id == oipId)
                .Select(p => p.OrderItem.OrderId)
                .SingleAsync();
            await TestDataSeeder.SetOrderStatusAsync(Factory, orderId, OrderStatus.Active);
        }
        return (t, oipId);
    }

    private async Task<List<Guid>> GetNotificationRecipientsAsync(Guid tenantId, NotificationType type)
    {
        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await ordersDb.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.TenantId == tenantId && n.Type == type)
            .Select(n => n.UserId)
            .ToListAsync();
    }

    private async Task<Guid> GetMostRecentBlockRequestIdAsync(Guid tenantId)
    {
        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await ordersDb.BlockRequests
            .IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => b.Id)
            .FirstAsync();
    }
}
