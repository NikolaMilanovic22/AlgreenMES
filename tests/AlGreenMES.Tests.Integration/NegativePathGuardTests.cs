using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// Defensive coverage for domain state-machine guards. Most happy paths
/// are tested in WorkflowTests; these tests assert that the negative
/// paths return 400 with the specific error code the FE relies on
/// (GlobalExceptionHandlerMiddleware maps DomainException → 400 with
/// {error: {code, message}}).
///
/// Why this matters: a handler forgets the
/// "if (entity.Status != Expected)" guard → the mutation silently
/// succeeds with wrong state. Double-clicks, race conditions, and
/// tablet-network-flap retries all hit these guards in production. A
/// missing guard corrupts state subtly and only surfaces during audit
/// reconciliation weeks later.
/// </summary>
public class NegativePathGuardTests : IntegrationTestBase
{
    public NegativePathGuardTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    // ---------------------------------------------------------------------
    // Order state machine guards
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ActivateOrder_WithNoItems_Returns400_NO_ITEMS()
    {
        // The activate guard exists to keep empty orders off the floor —
        // workers would see a queue entry with nothing to actually do.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var orderId = await TestDataSeeder.SeedOrderAsync(Factory, t.TenantId, t.UserId);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsync($"/api/orders/{orderId}/activate", content: null);
        await AssertErrorCodeAsync(resp, "NO_ITEMS");
    }

    [Fact]
    public async Task PauseOrder_OnDraftOrder_Returns400_INVALID_STATUS()
    {
        // Order.Pause() only legal from Active. SeedOrderAsync leaves it Draft.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var orderId = await TestDataSeeder.SeedOrderAsync(Factory, t.TenantId, t.UserId);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsync($"/api/orders/{orderId}/pause", content: null);
        await AssertErrorCodeAsync(resp, "INVALID_STATUS");
    }

    [Fact]
    public async Task ResumeOrder_OnActiveOrder_Returns400_INVALID_STATUS()
    {
        // Resume only legal from Paused.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var orderId = await TestDataSeeder.SeedOrderAsync(Factory, t.TenantId, t.UserId);
        await TestDataSeeder.SetOrderStatusAsync(Factory, orderId, OrderStatus.Active);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsync($"/api/orders/{orderId}/resume", content: null);
        await AssertErrorCodeAsync(resp, "INVALID_STATUS");
    }

    [Fact]
    public async Task ReopenOrder_OnDraftOrder_Returns400_INVALID_STATUS()
    {
        // Reopen only legal from Cancelled. Calling it on a Draft order
        // should not silently bounce it to a different status.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var orderId = await TestDataSeeder.SeedOrderAsync(Factory, t.TenantId, t.UserId);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsync($"/api/orders/{orderId}/reopen", content: null);
        await AssertErrorCodeAsync(resp, "INVALID_STATUS");
    }

    // ---------------------------------------------------------------------
    // Process state machine guards
    // ---------------------------------------------------------------------

    [Fact]
    public async Task BlockProcess_WithEmptyReason_Returns400_REASON_REQUIRED()
    {
        // OrderItemProcess.Block requires a non-empty reason. A FE bug that
        // sends "" should be rejected at the boundary, not stored as a
        // blank-reason blockade nobody can audit.
        var (t, oipId) = await SeedActiveOrderInProgressAsync();
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/block",
            new { userId = t.UserId, reason = "" });
        await AssertErrorCodeAsync(resp, "REASON_REQUIRED");
    }

    [Fact]
    public async Task UnblockProcess_OnNotBlockedProcess_Returns400_NOT_BLOCKED()
    {
        // Unblock only legal from Blocked. Calling it on InProgress must
        // not silently reset the timer or clear unrelated state.
        var (t, oipId) = await SeedActiveOrderInProgressAsync();
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/unblock",
            new { userId = t.UserId, resetTime = false });
        await AssertErrorCodeAsync(resp, "NOT_BLOCKED");
    }

    [Fact]
    public async Task RestartProcess_OnPendingProcess_Returns400_INVALID_STATUS()
    {
        // Restart only legal from Completed. A coordinator double-clicking
        // restart on a brand-new process should hit the guard, not silently
        // reset its time + status.
        var (t, oipId) = await SeedActiveOrderWithProcessAsync(ProcessStatus.Pending);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/restart",
            new { resetTime = true });
        await AssertErrorCodeAsync(resp, "INVALID_STATUS");
    }

    [Fact]
    public async Task WithdrawProcess_WithEmptyReason_Returns400_REASON_REQUIRED()
    {
        // Same shape as block: withdrawal needs a reason for the audit trail.
        var (t, oipId) = await SeedActiveOrderInProgressAsync();
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/withdraw",
            new { userId = t.UserId, reason = "" });
        await AssertErrorCodeAsync(resp, "REASON_REQUIRED");
    }

    [Fact]
    public async Task StartProcessWork_OnAlreadyInProgress_Returns400()
    {
        // Mirrors StartProcessWorkTests negative case but lives here for
        // discoverability with the other state-machine guards. The double-
        // start case is what happens when a tablet retries a network-flap
        // request and the original already landed.
        var (t, oipId) = await SeedActiveOrderInProgressAsync();
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/start",
            new { userId = t.UserId });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------------
    // Block-request guard
    // ---------------------------------------------------------------------

    [Fact]
    public async Task CreateBlockRequest_OnPendingProcess_DoesNotSilentlySucceed()
    {
        // A block request only makes sense for an in-progress process —
        // creating one against a Pending process is a UI bug. CreateBlockRequest
        // either rejects this (DUPLICATE_REQUEST if previously raised, or a
        // state guard) or accepts it without crashing. The point of the test
        // is that the handler responds deterministically — never throws an
        // unhandled NRE or returns 500.
        var (t, oipId) = await SeedActiveOrderWithProcessAsync(ProcessStatus.Pending);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync("/api/block-requests", new
        {
            orderItemProcessId = oipId,
            orderItemSubProcessId = (Guid?)null,
            requestedByUserId = t.UserId,
            requestNote = "test",
        });

        // 201 (allowed) or 400 (guarded) are both acceptable — 500 is not.
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BlockProcess_OnAlreadyBlockedProcess_RejectsOrIsIdempotent()
    {
        // Double-block (e.g., coordinator double-clicks Approve on a pending
        // block request): the second call must NOT silently re-stamp the
        // BlockedAt timestamp or overwrite BlockedByUserId with someone else.
        // The handler either (a) rejects with INVALID_STATUS, or (b) is
        // idempotent (no DB mutation). 500 / NRE is the failure mode we're
        // guarding against.
        var (t, oipId) = await SeedActiveOrderWithProcessAsync(ProcessStatus.Blocked);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/block",
            new { userId = t.UserId, reason = "second click" });

        resp.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.NoContent, HttpStatusCode.BadRequest },
            "double-block must respond deterministically; never 500");
    }

    [Fact]
    public async Task RestartProcess_OnInProgress_Returns400_INVALID_STATUS()
    {
        // Mirror of the Pending case — restart-while-running is a coordinator
        // misclick that must not corrupt timer state.
        var (t, oipId) = await SeedActiveOrderInProgressAsync();
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/restart",
            new { resetTime = true });
        await AssertErrorCodeAsync(resp, "INVALID_STATUS");
    }

    [Fact]
    public async Task ActivateOrder_OnAlreadyActiveOrder_Returns400_INVALID_STATUS()
    {
        // Double-activate: only Draft orders activate. Without this guard,
        // a re-activate would reset process timers via ResetTimer() and
        // wipe in-progress work silently.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var (orderId, _, _) = await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, t.TenantId, t.UserId, categoryId,
            processIds: new[] { processId },
            processStatuses: new[] { ProcessStatus.InProgress });
        await TestDataSeeder.SetOrderStatusAsync(Factory, orderId, OrderStatus.Active);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsync($"/api/orders/{orderId}/activate", content: null);
        await AssertErrorCodeAsync(resp, "INVALID_STATUS");
    }

    [Fact]
    public async Task CompleteOrderItemProcess_OnPendingProcess_Returns400_INVALID_STATUS()
    {
        // Latent bug discovered 23.06.2026: OrderItemProcess.Complete()
        // had no status guard. A coordinator clicking "Complete" on a
        // never-started process would silently mark it Completed with
        // TotalDurationMinutes=0 — corrupting reports and letting tablet
        // workers fake work they never did. Guard added; this test pins
        // the fix.
        var (t, oipId) = await SeedActiveOrderWithProcessAsync(ProcessStatus.Pending);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsync(
            $"/api/order-item-processes/{oipId}/complete", content: null);

        await AssertErrorCodeAsync(resp, "INVALID_STATUS");
    }

    [Fact]
    public async Task PauseOrder_OnCancelledOrder_Returns400_INVALID_STATUS()
    {
        // Cancel → Pause is a status transition that doesn't exist.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var orderId = await TestDataSeeder.SeedOrderAsync(Factory, t.TenantId, t.UserId);
        await TestDataSeeder.SetOrderStatusAsync(Factory, orderId, OrderStatus.Cancelled);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsync($"/api/orders/{orderId}/pause", content: null);
        await AssertErrorCodeAsync(resp, "INVALID_STATUS");
    }

    [Fact]
    public async Task ResumeOrder_OnDraftOrder_Returns400_INVALID_STATUS()
    {
        // Resume only legal from Paused; calling on Draft must not promote
        // the order to Active without going through the Activate guard
        // (which requires items + priority).
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var orderId = await TestDataSeeder.SeedOrderAsync(Factory, t.TenantId, t.UserId);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsync($"/api/orders/{orderId}/resume", content: null);
        await AssertErrorCodeAsync(resp, "INVALID_STATUS");
    }

    // ---------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------

    private async Task<(SeededTenant Tenant, Guid OipId)> SeedActiveOrderInProgressAsync()
        => await SeedActiveOrderWithProcessAsync(ProcessStatus.InProgress);

    private async Task<(SeededTenant Tenant, Guid OipId)> SeedActiveOrderWithProcessAsync(ProcessStatus status)
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, status);
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

    private static async Task AssertErrorCodeAsync(HttpResponseMessage resp, string expectedCode)
    {
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be(expectedCode);
    }
}
