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
/// Covers the ActionId idempotency key on workflow endpoints. When the network
/// drops mid-request the tablet can't tell "never reached server" from "applied
/// but response lost"; it retries with the SAME ActionId. The server must apply
/// the action exactly once — the replay is a clean no-op returning current
/// state, NOT a double-apply (corrupted timer) and NOT a confusing error.
/// Without an ActionId (every caller today) the pre-existing state guards still
/// fire — asserted by the last test.
/// </summary>
public class IdempotencyTests : IntegrationTestBase
{
    public IdempotencyTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Replayed_complete_with_same_ActionId_is_noop_and_does_not_double_count()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var oipId = await SeedActivePendingOipAsync(t);

        var now = DateTime.UtcNow;
        var startActionId = Guid.NewGuid();
        var completeActionId = Guid.NewGuid();

        var startResp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/start",
            new { userId = t.UserId, occurredAt = now.AddMinutes(-30), actionId = startActionId });
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var completeResp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/complete",
            new { occurredAt = now.AddMinutes(-10), actionId = completeActionId });
        completeResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Replay the SAME complete (lost-response retry). Different occurredAt to
        // prove the replay is ignored entirely — not re-applied with new data.
        var replayResp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/complete",
            new { occurredAt = now, actionId = completeActionId });
        replayResp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "a replayed action is a clean no-op, not the INVALID_STATUS error a re-run would throw");

        var oip = await GetOipAsync(oipId);
        oip.Status.Should().Be(ProcessStatus.Completed);
        oip.TotalDurationMinutes.Should().Be(20 * 60, "duration is counted once, not doubled by the replay");
    }

    [Fact]
    public async Task Replayed_start_with_same_ActionId_returns_current_state_without_duplicate_log()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var oipId = await SeedActivePendingOipAsync(t);

        var actionId = Guid.NewGuid();
        var body = new { userId = t.UserId, actionId };

        var first = await client.PostAsJsonAsync($"/api/order-item-processes/{oipId}/start", body);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Replay: would normally throw "can only start pending processes".
        var replay = await client.PostAsJsonAsync($"/api/order-item-processes/{oipId}/start", body);
        replay.StatusCode.Should().Be(HttpStatusCode.OK, "replay returns current state, not an error");

        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var logCount = await ordersDb.OrderItemProcessLogs
            .IgnoreQueryFilters()
            .CountAsync(l => l.OrderItemProcessId == oipId);
        logCount.Should().Be(1, "the replay must NOT open a second timing log");
    }

    [Fact]
    public async Task Replayed_subprocess_complete_with_same_ActionId_does_not_double_count()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.InProgress);
        await SetParentOrderActiveAsync(oipId);
        var oispId = await TestDataSeeder.SeedOrderItemSubProcessAsync(Factory, oipId, processId);

        var now = DateTime.UtcNow;
        var completeActionId = Guid.NewGuid();

        var startResp = await client.PostAsJsonAsync(
            $"/api/order-item-sub-processes/{oispId}/start",
            new { userId = t.UserId, occurredAt = now.AddMinutes(-25), actionId = Guid.NewGuid() });
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var completeResp = await client.PostAsJsonAsync(
            $"/api/order-item-sub-processes/{oispId}/complete",
            new { userId = t.UserId, occurredAt = now.AddMinutes(-10), actionId = completeActionId });
        completeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var replayResp = await client.PostAsJsonAsync(
            $"/api/order-item-sub-processes/{oispId}/complete",
            new { userId = t.UserId, occurredAt = now, actionId = completeActionId });
        replayResp.StatusCode.Should().Be(HttpStatusCode.OK, "replay returns current state");

        var oisp = await GetOispAsync(oispId);
        oisp.Status.Should().Be(SubProcessStatus.Completed);
        oisp.TotalDurationMinutes.Should().Be(15 * 60, "duration counted once despite the replay");
    }

    [Fact]
    public async Task Without_ActionId_a_duplicate_complete_still_errors()
    {
        // Idempotency is opt-in. With no ActionId — the only path any caller
        // uses today — the existing state guard must still reject a re-complete.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var oipId = await SeedActivePendingOipAsync(t);

        var startResp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/start", new { userId = t.UserId });
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstComplete = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/complete", new { });
        firstComplete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var secondComplete = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/complete", new { });
        secondComplete.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "without an idempotency key the domain guard still rejects completing an already-completed process");
    }

    private async Task<Guid> SeedActivePendingOipAsync(SeededTenant t)
    {
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.Pending);
        await SetParentOrderActiveAsync(oipId);
        return oipId;
    }

    private async Task SetParentOrderActiveAsync(Guid orderItemProcessId)
    {
        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var orderId = await ordersDb.OrderItemProcesses
            .IgnoreQueryFilters()
            .Where(p => p.Id == orderItemProcessId)
            .Select(p => p.OrderItem.OrderId)
            .SingleAsync();
        await ordersDb.Orders
            .IgnoreQueryFilters()
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Active));
    }

    private async Task<AlGreenMES.Modules.Orders.Domain.Entities.OrderItemProcess> GetOipAsync(Guid oipId)
    {
        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await ordersDb.OrderItemProcesses.IgnoreQueryFilters().SingleAsync(p => p.Id == oipId);
    }

    private async Task<AlGreenMES.Modules.Orders.Domain.Entities.OrderItemSubProcess> GetOispAsync(Guid oispId)
    {
        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await ordersDb.OrderItemSubProcesses.IgnoreQueryFilters().Include(sp => sp.Logs).SingleAsync(sp => sp.Id == oispId);
    }
}
