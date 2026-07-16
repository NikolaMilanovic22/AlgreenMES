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
/// Covers the optional client-supplied OccurredAt on workflow endpoints — the
/// backbone of tablet offline support. A tablet action taken while offline is
/// replayed minutes later; the recorded work time must reflect when the worker
/// actually tapped, NOT when the server processed the replay. When OccurredAt
/// is omitted (every caller today), the server falls back to "now", i.e. the
/// pre-existing behaviour — asserted by the second test.
/// </summary>
public class OfflineOccurredAtTests : IntegrationTestBase
{
    public OfflineOccurredAtTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task OccurredAt_drives_duration_for_start_and_complete()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.Pending);
        await SetParentOrderActiveAsync(oipId);

        // The worker started 30 min ago and completed 10 min ago, both while
        // offline; the queue is only now replaying them. Real elapsed = 20 min.
        var now = DateTime.UtcNow;
        var startedAt = now.AddMinutes(-30);
        var completedAt = now.AddMinutes(-10);

        var startResp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/start",
            new { userId = t.UserId, occurredAt = startedAt });
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var completeResp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/complete",
            new { occurredAt = completedAt });
        completeResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var oip = await ordersDb.OrderItemProcesses
            .IgnoreQueryFilters()
            .SingleAsync(p => p.Id == oipId);

        // StartedAt reflects the tap time, not the replay time.
        oip.StartedAt.Should().BeCloseTo(startedAt, TimeSpan.FromSeconds(1));
        // Duration (field name says Minutes but stores SECONDS — legacy) is the
        // real 20-minute gap, NOT ~0 as it would be if the server used "now"
        // for both replayed calls.
        oip.TotalDurationMinutes.Should().Be(20 * 60);

        // The process log spans the real work window too.
        var log = await ordersDb.OrderItemProcessLogs
            .IgnoreQueryFilters()
            .SingleAsync(l => l.OrderItemProcessId == oipId);
        log.StartTime.Should().BeCloseTo(startedAt, TimeSpan.FromSeconds(1));
        log.EndTime.Should().BeCloseTo(completedAt, TimeSpan.FromSeconds(1));
        log.DurationSeconds.Should().Be(20 * 60);
    }

    [Fact]
    public async Task Omitting_OccurredAt_falls_back_to_now()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.Pending);
        await SetParentOrderActiveAsync(oipId);

        // No occurredAt — the current production path. Start + complete happen
        // within the same test moment, so recorded duration is ~0.
        var startResp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/start",
            new { userId = t.UserId });
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var completeResp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/complete",
            new { });
        completeResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var oip = await ordersDb.OrderItemProcesses
            .IgnoreQueryFilters()
            .SingleAsync(p => p.Id == oipId);

        oip.Status.Should().Be(ProcessStatus.Completed);
        oip.StartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        oip.TotalDurationMinutes.Should().BeLessThan(60); // seconds; near-zero
    }

    [Fact]
    public async Task SubProcess_start_and_complete_honor_OccurredAt()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.InProgress);
        await SetParentOrderActiveAsync(oipId);
        var oispId = await TestDataSeeder.SeedOrderItemSubProcessAsync(Factory, oipId, processId);

        // Worker ran this sub-process from 25 min ago to 10 min ago — both taps
        // offline, replayed now. Real elapsed = 15 min.
        var now = DateTime.UtcNow;
        var startedAt = now.AddMinutes(-25);
        var completedAt = now.AddMinutes(-10);

        var startResp = await client.PostAsJsonAsync(
            $"/api/order-item-sub-processes/{oispId}/start",
            new { userId = t.UserId, occurredAt = startedAt });
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var completeResp = await client.PostAsJsonAsync(
            $"/api/order-item-sub-processes/{oispId}/complete",
            new { userId = t.UserId, occurredAt = completedAt });
        completeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var oisp = await GetOispAsync(oispId);
        oisp.Status.Should().Be(SubProcessStatus.Completed);
        var log = oisp.Logs.Single();
        log.StartTime.Should().BeCloseTo(startedAt, TimeSpan.FromSeconds(1));
        log.EndTime.Should().BeCloseTo(completedAt, TimeSpan.FromSeconds(1));
        log.DurationMinutes.Should().Be(15 * 60); // field stores seconds (legacy)
        oisp.TotalDurationMinutes.Should().Be(15 * 60);
    }

    [Fact]
    public async Task Stop_process_without_subprocess_honors_OccurredAt()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.Pending);
        await SetParentOrderActiveAsync(oipId);

        // Started 40 min ago, paused (stop) 15 min ago. Real elapsed = 25 min.
        var now = DateTime.UtcNow;
        var startedAt = now.AddMinutes(-40);
        var stoppedAt = now.AddMinutes(-15);

        var startResp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/start",
            new { userId = t.UserId, occurredAt = startedAt });
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var stopResp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/stop",
            new { userId = t.UserId, occurredAt = stoppedAt });
        stopResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var oip = await GetOipAsync(oipId);
        oip.PausedAt.Should().BeCloseTo(stoppedAt, TimeSpan.FromSeconds(1));
        oip.TotalDurationMinutes.Should().Be(25 * 60); // seconds

        var log = await GetSingleProcessLogAsync(oipId);
        log.EndTime.Should().BeCloseTo(stoppedAt, TimeSpan.FromSeconds(1));
        log.DurationSeconds.Should().Be(25 * 60);
    }

    [Fact]
    public async Task Stop_process_with_subprocess_ends_open_log_at_OccurredAt()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.InProgress);
        await SetParentOrderActiveAsync(oipId);
        var oispId = await TestDataSeeder.SeedOrderItemSubProcessAsync(Factory, oipId, processId);

        // Sub-process started 35 min ago; worker stopped 5 min ago. Elapsed 30.
        var now = DateTime.UtcNow;
        var startedAt = now.AddMinutes(-35);
        var stoppedAt = now.AddMinutes(-5);

        var startResp = await client.PostAsJsonAsync(
            $"/api/order-item-sub-processes/{oispId}/start",
            new { userId = t.UserId, occurredAt = startedAt });
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var stopResp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/stop",
            new { userId = t.UserId, occurredAt = stoppedAt });
        stopResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var oisp = await GetOispAsync(oispId);
        var log = oisp.Logs.Single();
        log.EndTime.Should().BeCloseTo(stoppedAt, TimeSpan.FromSeconds(1));
        log.DurationMinutes.Should().Be(30 * 60); // seconds
        oisp.TotalDurationMinutes.Should().Be(30 * 60);
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

    private async Task<AlGreenMES.Modules.Orders.Domain.Entities.OrderItemProcessLog> GetSingleProcessLogAsync(Guid oipId)
    {
        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await ordersDb.OrderItemProcessLogs
            .IgnoreQueryFilters()
            .SingleAsync(l => l.OrderItemProcessId == oipId);
    }
}
