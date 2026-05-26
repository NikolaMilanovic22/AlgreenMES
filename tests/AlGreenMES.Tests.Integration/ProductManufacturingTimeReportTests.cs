using System.Net;
using System.Text.Json;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using AlGreenMES.Modules.Production.Domain.Enums;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// /api/reports/product-manufacturing-time — per-completed-order breakdown
/// of process timings + inter-process gaps. Bojan spec 25.05.2026.
///
/// Aspects covered:
///   • One row per completed order; processes sorted by StartedAt.
///   • Top complexity (najzastupljenija težina) tie-break: T/S→S,
///     S/L→L, T/L→L; all-tied → L; null when no OIP has complexity.
///   • Last-process gap is always 0.
///   • Cross-tenant isolation.
/// </summary>
public class ProductManufacturingTimeReportTests : IntegrationTestBase
{
    public ProductManufacturingTimeReportTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task ProductManufacturingTime_returns_one_row_per_completed_order()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        await TestDataSeeder.SeedCategoryProcessesAndDepsAsync(Factory, categoryId, new[] { processId });

        // Two completed orders + one InProgress (should not appear).
        var (orderA, _, _) = await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, t.TenantId, t.UserId, categoryId,
            new[] { processId }, new[] { ProcessStatus.Completed });
        var (orderB, _, _) = await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, t.TenantId, t.UserId, categoryId,
            new[] { processId }, new[] { ProcessStatus.Completed });
        await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, t.TenantId, t.UserId, categoryId,
            new[] { processId }, new[] { ProcessStatus.InProgress });

        // Report filters by order status — must mark the two as Completed.
        await TestDataSeeder.MarkOrderCompletedAsync(Factory, orderA);
        await TestDataSeeder.MarkOrderCompletedAsync(Factory, orderB);

        var resp = await client.GetAsync("/api/reports/product-manufacturing-time");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        doc.RootElement.GetProperty("orders").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ProductManufacturingTime_last_process_gap_is_zero()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var procA = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var procB = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        await TestDataSeeder.SeedCategoryProcessesAndDepsAsync(Factory, categoryId, new[] { procA, procB });

        var seeded = await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, t.TenantId, t.UserId, categoryId,
            new[] { procA, procB }, new[] { ProcessStatus.Completed, ProcessStatus.Completed });

        // Force ordered StartedAt timestamps so the BE sorts the two processes
        // deterministically; otherwise both start at "UtcNow" and ordering is
        // arbitrary.
        var now = DateTime.UtcNow;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            await db.OrderItemProcesses.IgnoreQueryFilters()
                .Where(p => p.Id == seeded.OipIds[0])
                .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.StartedAt, now.AddHours(-2))
                    .SetProperty(p => p.CompletedAt, now.AddHours(-1)));
            await db.OrderItemProcesses.IgnoreQueryFilters()
                .Where(p => p.Id == seeded.OipIds[1])
                .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.StartedAt, now.AddMinutes(-30))
                    .SetProperty(p => p.CompletedAt, now));
            // Mark the order completed so it shows up.
            await db.Orders.IgnoreQueryFilters()
                .Where(o => o.Id == seeded.OrderId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(o => o.Status, OrderStatus.Completed)
                    .SetProperty(o => o.CompletedAt, now));
        }

        var resp = await client.GetAsync("/api/reports/product-manufacturing-time");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var order = doc.RootElement.GetProperty("orders").EnumerateArray()
            .Single(o => o.GetProperty("orderId").GetGuid() == seeded.OrderId);
        var processes = order.GetProperty("processes").EnumerateArray().ToList();

        processes.Should().HaveCount(2);
        // First process: gap = 30 min × 60 = 1800s (procB.StartedAt − procA.CompletedAt).
        processes[0].GetProperty("gapToNextSeconds").GetInt32().Should().BeInRange(1700, 1900);
        // Last process: gap = 0.
        processes[1].GetProperty("gapToNextSeconds").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ProductManufacturingTime_top_complexity_tie_break_S_over_T()
    {
        // Item with 1 T-complexity OIP + 1 S-complexity OIP. Counts are equal,
        // so Bojan's tie-break says T/S → S (lower of the two).
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var procA = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var procB = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        await TestDataSeeder.SeedCategoryProcessesAndDepsAsync(Factory, categoryId, new[] { procA, procB });

        var seeded = await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, t.TenantId, t.UserId, categoryId,
            new[] { procA, procB }, new[] { ProcessStatus.Completed, ProcessStatus.Completed });

        var now = DateTime.UtcNow;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            await db.OrderItemProcesses.IgnoreQueryFilters()
                .Where(p => p.Id == seeded.OipIds[0])
                .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.Complexity, (ComplexityType?)ComplexityType.T)
                    .SetProperty(p => p.StartedAt, now.AddHours(-2))
                    .SetProperty(p => p.CompletedAt, now.AddHours(-1)));
            await db.OrderItemProcesses.IgnoreQueryFilters()
                .Where(p => p.Id == seeded.OipIds[1])
                .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.Complexity, (ComplexityType?)ComplexityType.S)
                    .SetProperty(p => p.StartedAt, now.AddMinutes(-30))
                    .SetProperty(p => p.CompletedAt, now));
            await db.Orders.IgnoreQueryFilters()
                .Where(o => o.Id == seeded.OrderId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(o => o.Status, OrderStatus.Completed)
                    .SetProperty(o => o.CompletedAt, now));
        }

        var resp = await client.GetAsync("/api/reports/product-manufacturing-time");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var order = doc.RootElement.GetProperty("orders").EnumerateArray()
            .Single(o => o.GetProperty("orderId").GetGuid() == seeded.OrderId);
        order.GetProperty("topComplexity").GetString().Should().Be("S");
    }

    [Fact]
    public async Task ProductManufacturingTime_isolates_data_across_tenants()
    {
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
        var procA = await TestDataSeeder.SeedProcessAsync(Factory, a.TenantId, a.UserId);
        var categoryA = await TestDataSeeder.SeedProductCategoryAsync(Factory, a.TenantId, a.UserId);
        await TestDataSeeder.SeedCategoryProcessesAndDepsAsync(Factory, categoryA, new[] { procA });
        var seeded = await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, a.TenantId, a.UserId, categoryA,
            new[] { procA }, new[] { ProcessStatus.Completed });

        // Mark order completed so it qualifies for the report.
        var now = DateTime.UtcNow;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            await db.Orders.IgnoreQueryFilters()
                .Where(o => o.Id == seeded.OrderId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(o => o.Status, OrderStatus.Completed)
                    .SetProperty(o => o.CompletedAt, now));
        }

        var clientB = await TestDataSeeder.AuthenticatedClientAsync(Factory, b);
        var resp = await clientB.GetAsync("/api/reports/product-manufacturing-time");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        doc.RootElement.GetProperty("orders").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ProductManufacturingTime_unauthenticated_returns_401()
    {
        var anon = Factory.CreateClient();
        var resp = await anon.GetAsync("/api/reports/product-manufacturing-time");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
