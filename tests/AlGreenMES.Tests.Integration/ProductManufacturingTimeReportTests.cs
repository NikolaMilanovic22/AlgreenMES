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
        // Processes are returned ordered by SequenceOrder (matches the order table).
        processes[0].GetProperty("sequenceOrder").GetInt32()
            .Should().BeLessThanOrEqualTo(processes[1].GetProperty("sequenceOrder").GetInt32());
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
        // Zastupljenost težine: 1×T + 1×S of 2 OIPs → "50% / 50% / 0%" (T/S/L).
        order.GetProperty("complexityShare").GetString().Should().Be("50% / 50% / 0%");
    }

    [Fact]
    public async Task ProductManufacturingTime_emits_one_row_per_item_for_multi_item_order()
    {
        // Rows are per ORDER ITEM (29.05.2026): an order with 3 items must
        // produce 3 rows, one per item id — not a single order-level row.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        await TestDataSeeder.SeedCategoryProcessesAndDepsAsync(Factory, categoryId, new[] { processId });

        var (orderId, itemIds) = await TestDataSeeder.SeedMultiItemOrderAsync(
            Factory, t.TenantId, t.UserId, categoryId, processId, itemCount: 3);
        await TestDataSeeder.MarkOrderCompletedAsync(Factory, orderId);

        var resp = await client.GetAsync("/api/reports/product-manufacturing-time");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var rows = doc.RootElement.GetProperty("orders").EnumerateArray()
            .Where(o => o.GetProperty("orderId").GetGuid() == orderId).ToList();

        rows.Should().HaveCount(3);
        rows.Select(r => r.GetProperty("orderItemId").GetGuid())
            .Should().BeEquivalentTo(itemIds);
    }

    [Fact]
    public async Task ProductManufacturingTime_duration_uses_active_time_not_wallclock()
    {
        // Bojan 29.05.2026 (List 2 Q2): "Trajanje procesa" = the operator's
        // active time, NOT the wall-clock Start→Complete span. Seed a process
        // whose wall-clock span is 1h but whose active-timer total is 10 min;
        // the report must report 600s, not the 3600s span.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        await TestDataSeeder.SeedCategoryProcessesAndDepsAsync(Factory, categoryId, new[] { processId });

        var seeded = await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, t.TenantId, t.UserId, categoryId,
            new[] { processId }, new[] { ProcessStatus.Completed });

        var now = DateTime.UtcNow;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            await db.OrderItemProcesses.IgnoreQueryFilters()
                .Where(p => p.Id == seeded.OipIds[0])
                .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.StartedAt, now.AddHours(-1))
                    .SetProperty(p => p.CompletedAt, now)
                    // Active timer total = 10 min (stored as SECONDS in the
                    // misnamed "Minutes" column). No sub-processes → this is the
                    // effective duration the report should use.
                    .SetProperty(p => p.TotalDurationMinutes, 600));
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
        var process = order.GetProperty("processes").EnumerateArray().Single();
        // Active time (600s), NOT the 3600s wall-clock span.
        process.GetProperty("durationSeconds").GetInt32().Should().Be(600);
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

    [Fact]
    public async Task ProductManufacturingTime_row_carries_order_item_id()
    {
        // Rows are now per ORDER ITEM (29.05.2026) — the row exposes the
        // orderItemId so the FE can key rows uniquely when an order has
        // multiple items.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        await TestDataSeeder.SeedCategoryProcessesAndDepsAsync(Factory, categoryId, new[] { processId });

        var seeded = await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, t.TenantId, t.UserId, categoryId,
            new[] { processId }, new[] { ProcessStatus.Completed });
        await TestDataSeeder.MarkOrderCompletedAsync(Factory, seeded.OrderId);

        var resp = await client.GetAsync("/api/reports/product-manufacturing-time");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var row = doc.RootElement.GetProperty("orders").EnumerateArray()
            .Single(o => o.GetProperty("orderId").GetGuid() == seeded.OrderId);
        row.GetProperty("orderItemId").GetGuid().Should().Be(seeded.ItemId);
    }
}
