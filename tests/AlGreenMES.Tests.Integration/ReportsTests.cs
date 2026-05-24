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
}
