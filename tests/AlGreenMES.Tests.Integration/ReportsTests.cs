using System.Net;
using System.Net.Http.Json;
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

public class ReportsTests : IntegrationTestBase
{
    public ReportsTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ─── PATCH /api/order-item-processes/{id}/excluded-from-reports ──

    [Fact]
    public async Task PatchExcludedFromReports_persists_the_flag_in_db()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId);

        var resp = await client.PatchAsJsonAsync(
            $"/api/order-item-processes/{oipId}/excluded-from-reports",
            new { Excluded = true });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var oip = await db.OrderItemProcesses.IgnoreQueryFilters()
            .SingleAsync(p => p.Id == oipId);
        oip.IsExcludedFromReports.Should().BeTrue();
    }

    [Fact]
    public async Task PatchExcludedFromReports_can_toggle_back_to_false()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId);

        // Exclude, then re-include.
        var resp1 = await client.PatchAsJsonAsync(
            $"/api/order-item-processes/{oipId}/excluded-from-reports", new { Excluded = true });
        resp1.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var resp2 = await client.PatchAsJsonAsync(
            $"/api/order-item-processes/{oipId}/excluded-from-reports", new { Excluded = false });
        resp2.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var oip = await db.OrderItemProcesses.IgnoreQueryFilters()
            .SingleAsync(p => p.Id == oipId);
        oip.IsExcludedFromReports.Should().BeFalse();
    }

    [Fact]
    public async Task PatchExcludedFromReports_returns_404_for_unknown_id()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PatchAsJsonAsync(
            $"/api/order-item-processes/{Guid.NewGuid()}/excluded-from-reports",
            new { Excluded = true });

        // BE maps NotFoundException → 404 via GlobalExceptionHandler.
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PatchExcludedFromReports_blocks_cross_tenant_writes()
    {
        // Tenant A seeds an OIP. Tenant B's user must NOT be able to flip
        // its exclusion flag — that would leak across the tenant boundary
        // (Sprint 3.0 security guardrail).
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
        var processA = await TestDataSeeder.SeedProcessAsync(Factory, a.TenantId, a.UserId);
        var categoryA = await TestDataSeeder.SeedProductCategoryAsync(Factory, a.TenantId, a.UserId);
        var oipA = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, a.TenantId, a.UserId, processA, categoryA);

        var clientB = await TestDataSeeder.AuthenticatedClientAsync(Factory, b);

        var resp = await clientB.PatchAsJsonAsync(
            $"/api/order-item-processes/{oipA}/excluded-from-reports",
            new { Excluded = true });

        // Tenant B can't see tenant A's OIP — repository filter returns null,
        // handler throws NotFoundException → 404. The important property is
        // that the row is NOT mutated, NOT that the status code is 404 vs
        // 403 — either is acceptable as long as the write is rejected.
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var oip = await db.OrderItemProcesses.IgnoreQueryFilters()
            .SingleAsync(p => p.Id == oipA);
        oip.IsExcludedFromReports.Should().BeFalse();
    }

    // ─── GET /api/reports/process-times — IsExcludedFromReports respected ──

    [Fact]
    public async Task GetProcessTimes_excludes_rows_marked_as_excluded()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);

        // Two completed OIPs with very different durations. The huge one
        // would skew the average if not excluded.
        var smallOip = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId,
            status: ProcessStatus.Completed,
            totalDurationSeconds: 600,
            complexity: ComplexityType.S);
        var hugeOip = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId,
            status: ProcessStatus.Completed,
            totalDurationSeconds: 999_999,
            complexity: ComplexityType.S);

        // First: without exclusion, both rows count.
        var resp1 = await client.GetAsync("/api/reports/process-times");
        resp1.EnsureSuccessStatusCode();
        var body1 = await resp1.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(body1))
        {
            var s = doc.RootElement.GetProperty("processes")[0]
                .GetProperty("stats").GetProperty("S");
            s.GetProperty("count").GetInt32().Should().Be(2);
        }

        // Mark the huge one as excluded.
        var patchResp = await client.PatchAsJsonAsync(
            $"/api/order-item-processes/{hugeOip}/excluded-from-reports",
            new { Excluded = true });
        patchResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Now the report should only see the small one.
        var resp2 = await client.GetAsync("/api/reports/process-times");
        resp2.EnsureSuccessStatusCode();
        var body2 = await resp2.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(body2))
        {
            var s = doc.RootElement.GetProperty("processes")[0]
                .GetProperty("stats").GetProperty("S");
            s.GetProperty("count").GetInt32().Should().Be(1);
            // Average is now the small one's value in minutes (600s = 10min).
            s.GetProperty("avgMinutes").GetDouble().Should().BeApproximately(10.0, 0.1);
        }
    }

    // ─── GET /api/reports/time-tracking — surfaces IsExcludedFromReports ──

    [Fact]
    public async Task GetTimeTracking_returns_exclusion_flag_per_row()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId,
            status: ProcessStatus.Completed,
            totalDurationSeconds: 600,
            complexity: ComplexityType.S);

        // Initially false — FE renders the row as included.
        var resp1 = await client.GetAsync("/api/reports/time-tracking");
        resp1.EnsureSuccessStatusCode();
        using (var doc = JsonDocument.Parse(await resp1.Content.ReadAsStringAsync()))
        {
            var items = doc.RootElement.GetProperty("items");
            items.GetArrayLength().Should().Be(1);
            items[0].GetProperty("isExcludedFromReports").GetBoolean().Should().BeFalse();
        }

        // Toggle on — flag should now flip in the response.
        var patchResp = await client.PatchAsJsonAsync(
            $"/api/order-item-processes/{oipId}/excluded-from-reports",
            new { Excluded = true });
        patchResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var resp2 = await client.GetAsync("/api/reports/time-tracking");
        resp2.EnsureSuccessStatusCode();
        using (var doc = JsonDocument.Parse(await resp2.Content.ReadAsStringAsync()))
        {
            var items = doc.RootElement.GetProperty("items");
            // Note: time-tracking still SHOWS excluded rows (FE marks them
            // faded so user can toggle back). Only process-times filters them.
            items.GetArrayLength().Should().Be(1);
            items[0].GetProperty("isExcludedFromReports").GetBoolean().Should().BeTrue();
        }
    }

    // ─── Auth ──────────────────────────────────────────────

    [Fact]
    public async Task GetProcessTimes_unauthenticated_returns_401()
    {
        var anon = Factory.CreateClient();
        var resp = await anon.GetAsync("/api/reports/process-times");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── GET /api/reports/active-process-funnel ────────────
    //
    // The "ready" logic is the most complex code we wrote — a Pending OIP
    // counts as "Spreman za izvršavanje" only when ALL its dependencies
    // are Completed-or-Withdrawn. Below tests cover the main branches.

    [Fact]
    public async Task ActiveFunnel_pending_with_no_deps_counts_as_ready()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        await TestDataSeeder.SeedCategoryProcessesAndDepsAsync(Factory, categoryId, new[] { processId });

        await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId,
            status: ProcessStatus.Pending);

        var resp = await client.GetAsync("/api/reports/active-process-funnel");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var row = doc.RootElement.GetProperty("processes")
            .EnumerateArray()
            .Single(p => p.GetProperty("processId").GetGuid() == processId);
        row.GetProperty("readyCount").GetInt32().Should().Be(1);
        row.GetProperty("inProgressCount").GetInt32().Should().Be(0);
        row.GetProperty("blockedCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ActiveFunnel_pending_with_unmet_dep_is_NOT_counted_as_ready()
    {
        // Process B depends on process A. A is Pending. So B should NOT
        // count as ready (waiting on A). And the count of B in the funnel
        // should be 0 across all three statuses, since pending-waiting is
        // not a tracked status per Sale/Bojan's spec.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processA = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var processB = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        await TestDataSeeder.SeedCategoryProcessesAndDepsAsync(
            Factory,
            categoryId,
            new[] { processA, processB },
            new[] { (processB, processA) }); // B depends on A

        // Order with item that has both processes; both Pending.
        await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, t.TenantId, t.UserId, categoryId,
            new[] { processA, processB },
            new[] { ProcessStatus.Pending, ProcessStatus.Pending });

        var resp = await client.GetAsync("/api/reports/active-process-funnel");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var processes = doc.RootElement.GetProperty("processes").EnumerateArray().ToList();

        // A is Pending with no deps → ready.
        processes.Single(p => p.GetProperty("processId").GetGuid() == processA)
            .GetProperty("readyCount").GetInt32().Should().Be(1);
        // B is Pending but A (its dep) is not done → NOT ready (and not counted).
        processes.Single(p => p.GetProperty("processId").GetGuid() == processB)
            .GetProperty("readyCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ActiveFunnel_pending_with_dep_completed_counts_as_ready()
    {
        // A is Completed, B is Pending. B should now be ready (dep met).
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processA = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var processB = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        await TestDataSeeder.SeedCategoryProcessesAndDepsAsync(
            Factory,
            categoryId,
            new[] { processA, processB },
            new[] { (processB, processA) });

        await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, t.TenantId, t.UserId, categoryId,
            new[] { processA, processB },
            new[] { ProcessStatus.Completed, ProcessStatus.Pending });

        var resp = await client.GetAsync("/api/reports/active-process-funnel");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var processes = doc.RootElement.GetProperty("processes").EnumerateArray().ToList();

        // A is Completed → not in funnel at all (Completed is not an active status).
        processes.Single(p => p.GetProperty("processId").GetGuid() == processA)
            .GetProperty("readyCount").GetInt32().Should().Be(0);
        // B is Pending with A completed → ready.
        processes.Single(p => p.GetProperty("processId").GetGuid() == processB)
            .GetProperty("readyCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ActiveFunnel_counts_inprogress_and_blocked_separately()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        await TestDataSeeder.SeedCategoryProcessesAndDepsAsync(Factory, categoryId, new[] { processId });

        await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId,
            status: ProcessStatus.InProgress);
        await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId,
            status: ProcessStatus.Blocked);

        var resp = await client.GetAsync("/api/reports/active-process-funnel");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var row = doc.RootElement.GetProperty("processes")
            .EnumerateArray()
            .Single(p => p.GetProperty("processId").GetGuid() == processId);
        row.GetProperty("inProgressCount").GetInt32().Should().Be(1);
        row.GetProperty("blockedCount").GetInt32().Should().Be(1);
    }

    // ─── GET /api/reports/delivery-compliance ──────────────

    [Fact]
    public async Task DeliveryCompliance_on_time_when_completed_on_or_before_delivery_date()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        await TestDataSeeder.SeedCategoryProcessesAndDepsAsync(Factory, categoryId, new[] { processId });

        var deliveryDate = DateTime.UtcNow.Date.AddDays(7);
        // On-time: completed BEFORE delivery date.
        await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, t.TenantId, t.UserId, categoryId,
            new[] { processId }, new[] { ProcessStatus.Completed },
            deliveryDate: deliveryDate,
            completedAtOverride: deliveryDate.AddDays(-3));
        // On-time boundary: completed on delivery date itself (same day = on time).
        await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, t.TenantId, t.UserId, categoryId,
            new[] { processId }, new[] { ProcessStatus.Completed },
            deliveryDate: deliveryDate,
            completedAtOverride: deliveryDate);
        // Late: completed after delivery date.
        await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, t.TenantId, t.UserId, categoryId,
            new[] { processId }, new[] { ProcessStatus.Completed },
            deliveryDate: deliveryDate,
            completedAtOverride: deliveryDate.AddDays(2));

        var resp = await client.GetAsync(
            $"/api/reports/delivery-compliance?granularity=Week" +
            $"&from={DateTime.UtcNow.Date.AddDays(-30):yyyy-MM-dd}" +
            $"&to={DateTime.UtcNow.Date.AddDays(60):yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var buckets = doc.RootElement.GetProperty("buckets").EnumerateArray().ToList();

        var totalOnTime = buckets.Sum(b => b.GetProperty("onTimeCount").GetInt32());
        var totalLate = buckets.Sum(b => b.GetProperty("lateCount").GetInt32());
        totalOnTime.Should().Be(2);  // before + same-day
        totalLate.Should().Be(1);
    }

    // ─── Cross-tenant isolation for the chart endpoints ────

    [Fact]
    public async Task ChartEndpoints_isolate_data_across_tenants()
    {
        // Seed completed OIPs in tenant A, then call all 3 chart endpoints
        // as tenant B's user. Tenant B's responses must NOT see any of A's
        // data — total counts/buckets remain at 0 for B.
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
        var processA = await TestDataSeeder.SeedProcessAsync(Factory, a.TenantId, a.UserId);
        var categoryA = await TestDataSeeder.SeedProductCategoryAsync(Factory, a.TenantId, a.UserId);
        await TestDataSeeder.SeedCategoryProcessesAndDepsAsync(Factory, categoryA, new[] { processA });
        await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, a.TenantId, a.UserId, processA, categoryA,
            status: ProcessStatus.Completed,
            totalDurationSeconds: 600,
            complexity: ComplexityType.S);
        await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, a.TenantId, a.UserId, processA, categoryA,
            status: ProcessStatus.InProgress);

        var clientB = await TestDataSeeder.AuthenticatedClientAsync(Factory, b);

        // Delivery compliance: B should see zero buckets (no completed orders).
        var resp1 = await clientB.GetAsync(
            $"/api/reports/delivery-compliance?granularity=Week" +
            $"&from={DateTime.UtcNow.AddDays(-30):yyyy-MM-dd}&to={DateTime.UtcNow:yyyy-MM-dd}");
        resp1.EnsureSuccessStatusCode();
        using (var doc = JsonDocument.Parse(await resp1.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("buckets").GetArrayLength().Should().Be(0);
        }

        // Funnel: B should see no processes (B has no processes seeded at all)
        // OR processes with all-zero counts. Either way, no leaked totals.
        var resp2 = await clientB.GetAsync("/api/reports/active-process-funnel");
        resp2.EnsureSuccessStatusCode();
        using (var doc = JsonDocument.Parse(await resp2.Content.ReadAsStringAsync()))
        {
            var totalActive = doc.RootElement.GetProperty("processes").EnumerateArray()
                .Sum(p =>
                    p.GetProperty("inProgressCount").GetInt32()
                  + p.GetProperty("readyCount").GetInt32()
                  + p.GetProperty("blockedCount").GetInt32());
            totalActive.Should().Be(0);
        }

        // Trend: B can request the trend for A's processId (no auth on the
        // processId itself in our query — but the WHERE TenantId = B
        // filters out all data). Expect zero buckets + null normativ.
        var resp3 = await clientB.GetAsync(
            $"/api/reports/process-time-trend?processId={processA}" +
            $"&complexity=S&granularity=Week");
        resp3.EnsureSuccessStatusCode();
        using (var doc = JsonDocument.Parse(await resp3.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("buckets").GetArrayLength().Should().Be(0);
            doc.RootElement.GetProperty("normativMinutes").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }
}
