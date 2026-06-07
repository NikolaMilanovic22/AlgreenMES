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
/// /api/order-item-processes/{id}/start — added 07.06.2026 after a missing
/// ValueGeneratedNever config let a DbUpdateConcurrencyException through to
/// production. No end-to-end test was hitting the StartProcessWork happy
/// path. These tests do.
/// </summary>
public class StartProcessWorkTests : IntegrationTestBase
{
    public StartProcessWorkTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task StartProcessWork_for_process_without_subprocesses_creates_one_process_log()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.Pending);

        // StartProcessWork requires the parent order to be Active; the seeder
        // leaves it Pending (no order-lifecycle helper exists yet). Bump it
        // directly so we can isolate the start-process behaviour under test.
        await SetParentOrderActiveAsync(oipId);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/start",
            new { userId = t.UserId });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        // OIP transitioned + StartedByUserId populated (Bug B prerequisite).
        var oip = await ordersDb.OrderItemProcesses
            .IgnoreQueryFilters()
            .SingleAsync(p => p.Id == oipId);
        oip.Status.Should().Be(ProcessStatus.InProgress);
        oip.StartedByUserId.Should().Be(t.UserId);
        oip.StartedAt.Should().NotBeNull();

        // ProcessLog inserted + open (covers ValueGeneratedNever + the
        // Include(ProcessLogs) repository fix from 07.06).
        var logs = await ordersDb.OrderItemProcessLogs
            .IgnoreQueryFilters()
            .Where(l => l.OrderItemProcessId == oipId)
            .ToListAsync();
        logs.Should().HaveCount(1);
        logs[0].UserId.Should().Be(t.UserId);
        logs[0].EndTime.Should().BeNull();
    }

    [Fact]
    public async Task StartProcessWork_returns_500_or_400_when_OIP_is_not_pending()
    {
        // Guard: starting an already-started process should fail with a
        // domain error rather than silently corrupting state.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.InProgress);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/start",
            new { userId = t.UserId });

        // GlobalExceptionHandlerMiddleware maps DomainException → 400.
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
}
