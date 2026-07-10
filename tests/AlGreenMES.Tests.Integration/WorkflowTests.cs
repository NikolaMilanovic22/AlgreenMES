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
/// Integration coverage for the factory-floor workflow endpoints that were
/// flagged HIGH-risk in the 23.06.2026 endpoint-coverage audit — process
/// state machine (block/unblock/restart/withdraw), sub-process flow
/// (start/complete), order state mutations (pause/resume/reopen), tablet
/// pause, and the block-request + change-request approval flows. These are
/// the paths that, if broken, stop the factory floor (block/unblock/restart)
/// or leave coordinators unable to intervene (block-request approval).
///
/// Each test seeds the minimum domain state needed to drive the endpoint
/// from a real HTTP request and then asserts the resulting state via the
/// DB (skipping a follow-up GET avoids re-testing the read path tested
/// elsewhere). Negative paths (forbidden-role, wrong-status) are NOT
/// covered here — the goal is happy-path safety nets so refactors can't
/// silently break the worker flows. Auth gates are tested in
/// IdentityAuthzTests; tenant isolation in CrossTenant tests.
/// </summary>
public class WorkflowTests : IntegrationTestBase
{
    public WorkflowTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    // ---------------------------------------------------------------------
    // Process state machine: /api/order-item-processes/{id}/{block,unblock,
    // restart,withdraw}. These are the four state transitions a coordinator
    // can trigger from the dashboard; happy paths only.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task BlockProcess_OnInProgressProcess_TransitionsToBlocked()
    {
        var (t, oipId) = await SeedActiveOrderWithProcessAsync(ProcessStatus.InProgress);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/block",
            new { userId = t.UserId, reason = "test block" });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var oip = await GetOipAsync(oipId);
        oip.Status.Should().Be(ProcessStatus.Blocked);
        oip.BlockedByUserId.Should().Be(t.UserId);
        oip.BlockReason.Should().Be("test block");
    }

    [Fact]
    public async Task UnblockProcess_OnBlockedProcess_ReturnsToInProgressPaused()
    {
        var (t, oipId) = await SeedActiveOrderWithProcessAsync(ProcessStatus.Blocked);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/unblock",
            new { userId = t.UserId, resetTime = false });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var oip = await GetOipAsync(oipId);
        oip.Status.Should().Be(ProcessStatus.InProgress);
        oip.PausedAt.Should().NotBeNull("unblock puts the process back into InProgress+Paused so the worker can manually resume on the tablet");
        oip.UnblockedByUserId.Should().Be(t.UserId);
    }

    [Fact]
    public async Task RestartProcess_OnCompletedProcess_ResetsToInProgress()
    {
        var (t, oipId) = await SeedActiveOrderWithProcessAsync(ProcessStatus.Completed);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/restart",
            new { resetTime = true });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var oip = await GetOipAsync(oipId);
        oip.Status.Should().Be(ProcessStatus.InProgress);
        oip.CompletedAt.Should().BeNull("restart clears the completion stamp");
        oip.TotalDurationMinutes.Should().Be(0, "resetTime=true zeroes accumulated time");
    }

    [Fact]
    public async Task WithdrawProcess_OnInProgressProcess_MarksWithdrawn()
    {
        var (t, oipId) = await SeedActiveOrderWithProcessAsync(ProcessStatus.InProgress);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/withdraw",
            new { userId = t.UserId, reason = "moved to different worker" });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var oip = await GetOipAsync(oipId);
        oip.IsWithdrawn.Should().BeTrue();
        oip.Status.Should().Be(ProcessStatus.Withdrawn);
        oip.WithdrawnByUserId.Should().Be(t.UserId);
        oip.WithdrawnReason.Should().Be("moved to different worker");
    }

    // ---------------------------------------------------------------------
    // Sub-process flow: /api/order-item-sub-processes/{id}/{start,complete}.
    // Tablet workers hit these constantly; the complete handler also
    // auto-completes the parent OIP when all siblings are done.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task StartSubProcess_OnPendingSubProcess_TransitionsToInProgress()
    {
        var (t, oipId, processId) = await SeedActiveOrderWithProcessReturningProcessIdAsync(ProcessStatus.InProgress);
        var oispId = await TestDataSeeder.SeedOrderItemSubProcessAsync(Factory, oipId, processId);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-sub-processes/{oispId}/start",
            new { userId = t.UserId });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var oisp = await GetOispAsync(oispId);
        oisp.Status.Should().Be(SubProcessStatus.InProgress);
        oisp.Logs.Should().HaveCount(1, "start opens a new log entry");
        oisp.Logs.Single().EndTime.Should().BeNull("the log is open while the sub-process is running");
    }

    [Fact]
    public async Task CompleteSubProcess_OnLastInProgressSibling_AutoCompletesParent()
    {
        var (t, oipId, processId) = await SeedActiveOrderWithProcessReturningProcessIdAsync(ProcessStatus.InProgress);
        var oispId = await TestDataSeeder.SeedOrderItemSubProcessAsync(Factory, oipId, processId);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        // Start, then immediately complete — this is the only sub-process,
        // so completing it should auto-complete the parent OIP.
        var startResp = await client.PostAsJsonAsync(
            $"/api/order-item-sub-processes/{oispId}/start",
            new { userId = t.UserId });
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var completeResp = await client.PostAsJsonAsync(
            $"/api/order-item-sub-processes/{oispId}/complete",
            new { userId = t.UserId });

        completeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var oisp = await GetOispAsync(oispId);
        oisp.Status.Should().Be(SubProcessStatus.Completed);
        var oip = await GetOipAsync(oipId);
        oip.Status.Should().Be(ProcessStatus.Completed, "the parent OIP auto-completes when its last sub-process finishes");
    }

    // ---------------------------------------------------------------------
    // Order state mutations: pause/resume/reopen. Coordinators trigger
    // these from the dashboard; pause/resume gates by status, reopen is
    // the only path back from a cancelled order.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task PauseOrder_OnActiveOrder_TransitionsToPaused()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var orderId = await SeedActiveOrderAsync(t);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsync($"/api/orders/{orderId}/pause", content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var order = await GetOrderAsync(orderId);
        order.Status.Should().Be(OrderStatus.Paused);
    }

    [Fact]
    public async Task ResumeOrder_OnPausedOrder_TransitionsToActive()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var orderId = await SeedActiveOrderAsync(t);
        await TestDataSeeder.SetOrderStatusAsync(Factory, orderId, OrderStatus.Paused);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsync($"/api/orders/{orderId}/resume", content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var order = await GetOrderAsync(orderId);
        order.Status.Should().Be(OrderStatus.Active);
    }

    [Fact]
    public async Task ReopenOrder_OnCancelledOrder_ReturnsToDraft()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var orderId = await SeedActiveOrderAsync(t);
        await TestDataSeeder.SetOrderStatusAsync(Factory, orderId, OrderStatus.Cancelled);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsync($"/api/orders/{orderId}/reopen", content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var order = await GetOrderAsync(orderId);
        order.Status.Should().Be(OrderStatus.Draft, "reopen is the only path back from Cancelled — back to Draft so the coordinator can re-activate cleanly");
    }

    // ---------------------------------------------------------------------
    // Tablet pause: /api/tablet/pause closes any open sub-process logs for
    // the worker. ResumeOnLogin (already tested in ResumeOnLoginTests) does
    // the reverse; this guards the pause side of the auto-logout pair.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task TabletPause_WithActiveSubProcessLog_ClosesLogAndStampsPauseMarker()
    {
        var (t, oipId, processId) = await SeedActiveOrderWithProcessReturningProcessIdAsync(ProcessStatus.InProgress);
        var oispId = await TestDataSeeder.SeedOrderItemSubProcessAsync(Factory, oipId, processId);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        // Start the sub-process to get an open log
        var startResp = await client.PostAsJsonAsync(
            $"/api/order-item-sub-processes/{oispId}/start",
            new { userId = t.UserId });
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var pauseResp = await client.PostAsync($"/api/tablet/pause?userId={t.UserId}", content: null);

        pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var oisp = await GetOispAsync(oispId);
        oisp.Logs.Single().EndTime.Should().NotBeNull("pause closes the open log");
        oisp.PausedOnLogoutAt.Should().NotBeNull("pause stamps the marker so ResumeOnLogin can auto-resume");
    }

    // ---------------------------------------------------------------------
    // Block-request flow: worker creates a block request, coordinator
    // approves it. Approval transitions the underlying OIP to Blocked —
    // that's the "factory floor stops" path the dashboard surfaces.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task CreateBlockRequest_OnInProgressProcess_ReturnsCreatedWithPendingStatus()
    {
        var (t, oipId) = await SeedActiveOrderWithProcessAsync(ProcessStatus.InProgress);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync("/api/block-requests", new
        {
            orderItemProcessId = oipId,
            orderItemSubProcessId = (Guid?)null,
            requestedByUserId = t.UserId,
            requestNote = "machine broken",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ApproveBlockRequest_OnPending_BlocksTheUnderlyingProcess()
    {
        var (t, oipId) = await SeedActiveOrderWithProcessAsync(ProcessStatus.InProgress);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var createResp = await client.PostAsJsonAsync("/api/block-requests", new
        {
            orderItemProcessId = oipId,
            orderItemSubProcessId = (Guid?)null,
            requestedByUserId = t.UserId,
            requestNote = "needs supervisor decision",
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var brId = await GetMostRecentBlockRequestIdAsync(t.TenantId);

        var approveResp = await client.PostAsJsonAsync(
            $"/api/block-requests/{brId}/approve",
            new { handledByUserId = t.UserId, note = "approved by coord" });

        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var oip = await GetOipAsync(oipId);
        oip.Status.Should().Be(ProcessStatus.Blocked, "approving a block request blocks the underlying OIP — this is the path that makes the dashboard surface the stoppage");
    }

    // ---------------------------------------------------------------------
    // Change-request flow: ManagerOrSales creates a change request,
    // Coordinator+ approves. Coverage matches the block-request shape;
    // change-requests are tracking-only (no domain side-effect on approval).
    // ---------------------------------------------------------------------

    [Fact]
    public async Task CreateChangeRequest_AsManager_ReturnsCreatedWithPendingStatus()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Manager);
        var orderId = await SeedActiveOrderAsync(t);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync("/api/change-requests", new
        {
            orderId,
            requestedByUserId = t.UserId,
            requestType = "Modify",
            description = "change quantity from 5 to 7",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ApproveChangeRequest_OnPending_TransitionsToApproved()
    {
        // Manager creates the request, then the same Admin tenant approves
        // via CoordinatorUp. SeedTenantWithUserAsync(Admin) covers both
        // roles (Admin satisfies ManagerOrSales + CoordinatorUp).
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var orderId = await SeedActiveOrderAsync(t);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var createResp = await client.PostAsJsonAsync("/api/change-requests", new
        {
            orderId,
            requestedByUserId = t.UserId,
            requestType = "Modify",
            description = "change description",
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var crId = await GetMostRecentChangeRequestIdAsync(t.TenantId);

        var approveResp = await client.PostAsJsonAsync(
            $"/api/change-requests/{crId}/approve",
            new { handledByUserId = t.UserId, responseNote = "ok" });

        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cr = await GetChangeRequestAsync(crId);
        cr.Status.Should().Be(RequestStatus.Approved);
    }

    // ---------------------------------------------------------------------
    // Regression guards flagged by the 09.07.2026 coverage audit — state
    // transitions whose NEGATIVE / side-effect branches were unpinned.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task RestartProcess_ResetTimeFalse_PreservesAccumulatedDuration()
    {
        // Re-opening a finished process to add rework must NOT wipe already
        // recorded time (resetTime=true zeroes it — tested above; resetTime=false
        // keeps it). Payroll/report impact if this regresses.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId,
            ProcessStatus.Completed, totalDurationSeconds: 600);
        var orderId = await GetParentOrderIdAsync(oipId);
        await TestDataSeeder.SetOrderStatusAsync(Factory, orderId, OrderStatus.Active);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/restart",
            new { resetTime = false });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var oip = await GetOipAsync(oipId);
        oip.Status.Should().Be(ProcessStatus.InProgress);
        oip.CompletedAt.Should().BeNull("restart clears the completion stamp");
        oip.TotalDurationMinutes.Should().Be(600, "resetTime=false must preserve accumulated time");
    }

    [Fact]
    public async Task ApproveBlockRequest_AutoApprovesSiblingPendingRequestsOnSameProcess()
    {
        // Two workers filing block requests on the same process is common; the
        // handler auto-approves the other pending request(s) when one is
        // approved. If this regresses, the second request lingers Pending and a
        // coordinator chases a phantom.
        var (t, oipId) = await SeedActiveOrderWithProcessAsync(ProcessStatus.InProgress);
        var br1 = await TestDataSeeder.SeedBlockRequestAsync(Factory, t.TenantId, oipId, t.UserId);
        var br2 = await TestDataSeeder.SeedBlockRequestAsync(Factory, t.TenantId, oipId, t.UserId);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var approve = await client.PostAsJsonAsync(
            $"/api/block-requests/{br1}/approve",
            new { handledByUserId = t.UserId, note = "approved by coord" });
        approve.StatusCode.Should().Be(HttpStatusCode.OK);

        var statuses = await GetBlockRequestStatusesAsync(new[] { br1, br2 });
        statuses.Should().OnlyContain(s => s == RequestStatus.Approved,
            "approving one block request auto-approves the sibling pending request(s) for the same process");
        var oip = await GetOipAsync(oipId);
        oip.Status.Should().Be(ProcessStatus.Blocked);
    }

    [Fact]
    public async Task CompleteProcess_WithInProgressSubProcess_IsRejected()
    {
        // A parent OIP with sub-processes can only complete when all subs are
        // done. Completing while a sub timer runs would mark it Completed with
        // mis-attributed/zero sub time.
        var (t, oipId, processId) = await SeedActiveOrderWithProcessReturningProcessIdAsync(ProcessStatus.InProgress);
        var oispId = await TestDataSeeder.SeedOrderItemSubProcessAsync(Factory, oipId, processId);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        // Start the sub-process → InProgress (not complete).
        (await client.PostAsJsonAsync($"/api/order-item-sub-processes/{oispId}/start",
            new { userId = t.UserId })).EnsureSuccessStatusCode();

        var resp = await client.PostAsync($"/api/order-item-processes/{oipId}/complete", content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await resp.Content.ReadFromJsonAsync<WfErrorBody>();
        err!.Error.Code.Should().Be("SUBPROCESSES_NOT_COMPLETE");
        var oip = await GetOipAsync(oipId);
        oip.Status.Should().Be(ProcessStatus.InProgress, "completion was rejected — parent stays InProgress");
        oip.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task StartProcessWork_WhenDependencyNotMet_IsRejected()
    {
        // Core factory sequencing: a process can't start while a category
        // dependency predecessor is not yet Completed/Withdrawn.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var p1 = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var p2 = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        await TestDataSeeder.SeedCategoryProcessesAndDepsAsync(
            Factory, categoryId,
            new[] { p1, p2 },
            dependencies: new[] { (p2, p1) }); // p2 depends on p1

        var (orderId, _, oipIds) = await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, t.TenantId, t.UserId, categoryId,
            processIds: new[] { p1, p2 },
            processStatuses: new[] { ProcessStatus.Pending, ProcessStatus.Pending });
        await TestDataSeeder.SetOrderStatusAsync(Factory, orderId, OrderStatus.Active);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        // Start p2 while its predecessor p1 is still Pending → rejected.
        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipIds[1]}/start",
            new { userId = t.UserId });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await resp.Content.ReadFromJsonAsync<WfErrorBody>();
        err!.Error.Code.Should().Be("DEPENDENCY_NOT_MET");
    }

    private async Task<List<RequestStatus>> GetBlockRequestStatusesAsync(IEnumerable<Guid> ids)
    {
        var idList = ids.ToList();
        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await ordersDb.BlockRequests
            .IgnoreQueryFilters()
            .Where(b => idList.Contains(b.Id))
            .Select(b => b.Status)
            .ToListAsync();
    }

    private sealed record WfErrorBody(WfErrorPayload Error);
    private sealed record WfErrorPayload(string Code, string Message);

    // ---------------------------------------------------------------------
    // Shared setup helpers — keep the per-test bodies focused on the API
    // request + assertion, not the seeding ceremony.
    // ---------------------------------------------------------------------

    private async Task<(SeededTenant Tenant, Guid OipId)> SeedActiveOrderWithProcessAsync(ProcessStatus initialStatus)
    {
        var (t, oipId, _) = await SeedActiveOrderWithProcessReturningProcessIdAsync(initialStatus);
        return (t, oipId);
    }

    private async Task<(SeededTenant Tenant, Guid OipId, Guid ProcessId)> SeedActiveOrderWithProcessReturningProcessIdAsync(ProcessStatus initialStatus)
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, initialStatus);
        var orderId = await GetParentOrderIdAsync(oipId);
        await TestDataSeeder.SetOrderStatusAsync(Factory, orderId, OrderStatus.Active);
        return (t, oipId, processId);
    }

    private async Task<Guid> SeedActiveOrderAsync(SeededTenant t)
    {
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var (orderId, _, _) = await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, t.TenantId, t.UserId, categoryId,
            processIds: new[] { processId },
            processStatuses: new[] { ProcessStatus.Pending });
        await TestDataSeeder.SetOrderStatusAsync(Factory, orderId, OrderStatus.Active);
        return orderId;
    }

    private async Task<Guid> GetParentOrderIdAsync(Guid oipId)
    {
        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await ordersDb.OrderItemProcesses
            .IgnoreQueryFilters()
            .Where(p => p.Id == oipId)
            .Select(p => p.OrderItem.OrderId)
            .SingleAsync();
    }

    private async Task<AlGreenMES.Modules.Orders.Domain.Entities.OrderItemProcess> GetOipAsync(Guid oipId)
    {
        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await ordersDb.OrderItemProcesses
            .IgnoreQueryFilters()
            .SingleAsync(p => p.Id == oipId);
    }

    private async Task<AlGreenMES.Modules.Orders.Domain.Entities.OrderItemSubProcess> GetOispAsync(Guid oispId)
    {
        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await ordersDb.OrderItemSubProcesses
            .IgnoreQueryFilters()
            .Include(sp => sp.Logs)
            .SingleAsync(sp => sp.Id == oispId);
    }

    private async Task<AlGreenMES.Modules.Orders.Domain.Entities.Order> GetOrderAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await ordersDb.Orders
            .IgnoreQueryFilters()
            .SingleAsync(o => o.Id == orderId);
    }

    private async Task<AlGreenMES.Modules.Orders.Domain.Entities.ChangeRequest> GetChangeRequestAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await ordersDb.ChangeRequests
            .IgnoreQueryFilters()
            .SingleAsync(cr => cr.Id == id);
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

    private async Task<Guid> GetMostRecentChangeRequestIdAsync(Guid tenantId)
    {
        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await ordersDb.ChangeRequests
            .IgnoreQueryFilters()
            .Where(cr => cr.TenantId == tenantId)
            .OrderByDescending(cr => cr.CreatedAt)
            .Select(cr => cr.Id)
            .FirstAsync();
    }
}
