using System.Net;
using System.Text.Json;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// /api/reports/blocks-per-process — per-process block-request rollup with
/// working-hours average duration (intersection of CreatedAt → HandledAt
/// with active Shift windows). Bojan spec 25.05.2026.
///
/// Aspects covered:
///   • Submitted / approved / resolved / rejected counts roll up per process.
///   • Rejected blocks count toward TotalSubmitted but contribute zero
///     duration to the average.
///   • Approved = Approved + Resolved (per Bojan).
///   • Cross-tenant isolation (no leakage of A's blocks into B's response).
/// </summary>
public class BlocksPerProcessReportTests : IntegrationTestBase
{
    public BlocksPerProcessReportTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task BlocksPerProcess_rolls_up_counts_per_process()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);

        // 3 blocks against the same OIPs of the same process:
        //   1 approved, 1 resolved (counts as approved+resolved), 1 rejected.
        var oip1 = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, status: ProcessStatus.InProgress);
        var oip2 = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, status: ProcessStatus.InProgress);
        var oip3 = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, status: ProcessStatus.InProgress);

        await TestDataSeeder.SeedBlockRequestAsync(Factory, t.TenantId, oip1, t.UserId, RequestStatus.Approved);
        await TestDataSeeder.SeedBlockRequestAsync(Factory, t.TenantId, oip2, t.UserId, RequestStatus.Resolved);
        await TestDataSeeder.SeedBlockRequestAsync(Factory, t.TenantId, oip3, t.UserId, RequestStatus.Rejected);

        var resp = await client.GetAsync("/api/reports/blocks-per-process");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var row = doc.RootElement.GetProperty("processes")
            .EnumerateArray()
            .Single(p => p.GetProperty("processId").GetGuid() == processId);

        row.GetProperty("totalSubmitted").GetInt32().Should().Be(3);
        // Approved (BE field) = Approved + Resolved per Bojan's roll-up rule.
        row.GetProperty("approvedCount").GetInt32().Should().Be(2);
        row.GetProperty("resolvedCount").GetInt32().Should().Be(1);
        row.GetProperty("rejectedCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task BlocksPerProcess_isolates_data_across_tenants()
    {
        // Tenant A creates a block; tenant B's response must show 0 for the
        // same process id, OR not include that process at all (B's tenant
        // doesn't own the process). Either way: B sees no submitted count
        // that came from A's data.
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
        var processA = await TestDataSeeder.SeedProcessAsync(Factory, a.TenantId, a.UserId);
        var categoryA = await TestDataSeeder.SeedProductCategoryAsync(Factory, a.TenantId, a.UserId);
        var oipA = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, a.TenantId, a.UserId, processA, categoryA, status: ProcessStatus.InProgress);
        await TestDataSeeder.SeedBlockRequestAsync(Factory, a.TenantId, oipA, a.UserId, RequestStatus.Approved);

        var clientB = await TestDataSeeder.AuthenticatedClientAsync(Factory, b);
        var resp = await clientB.GetAsync("/api/reports/blocks-per-process");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        // B's tenant either gets back a zero-count row for processA (if the
        // process list endpoint surfaced it across tenants — it shouldn't),
        // or an empty processes array. The invariant is: total submitted = 0.
        var totalSubmitted = doc.RootElement.GetProperty("processes").EnumerateArray()
            .Sum(p => p.GetProperty("totalSubmitted").GetInt32());
        totalSubmitted.Should().Be(0);
    }

    [Fact]
    public async Task BlocksPerProcess_duration_counts_working_hours_not_wallclock()
    {
        // A block open 06:00 → 22:00 spans 16h of wall-clock time, but with a
        // single active shift 06:00–14:00 only 8 working hours should count.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId, startTime: new TimeOnly(6, 0), endTime: new TimeOnly(14, 0));
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        var oip = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, status: ProcessStatus.InProgress);

        var created = new DateTime(2026, 5, 4, 6, 0, 0, DateTimeKind.Utc);
        var handled = new DateTime(2026, 5, 4, 22, 0, 0, DateTimeKind.Utc);
        await TestDataSeeder.SeedBlockRequestAsync(
            Factory, t.TenantId, oip, t.UserId, RequestStatus.Resolved, created, handled);

        var resp = await client.GetAsync("/api/reports/blocks-per-process");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var row = doc.RootElement.GetProperty("processes").EnumerateArray()
            .Single(p => p.GetProperty("processId").GetGuid() == processId);

        // 8 working hours (06–14), NOT the 16h wall-clock span.
        row.GetProperty("averageDurationHours").GetDouble().Should().BeApproximately(8.0, 0.1);
    }

    [Fact]
    public async Task BlocksPerProcess_overlapping_active_shifts_are_unioned_not_summed()
    {
        // Two overlapping active shifts (06–14 and 10–18). A block open
        // 06:00 → 18:00 should count the UNION of the windows (12h), not the
        // sum of both intersections (8h + 8h = 16h).
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId, startTime: new TimeOnly(6, 0), endTime: new TimeOnly(14, 0));
        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId, startTime: new TimeOnly(10, 0), endTime: new TimeOnly(18, 0));
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        var oip = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, status: ProcessStatus.InProgress);

        var created = new DateTime(2026, 5, 4, 6, 0, 0, DateTimeKind.Utc);
        var handled = new DateTime(2026, 5, 4, 18, 0, 0, DateTimeKind.Utc);
        await TestDataSeeder.SeedBlockRequestAsync(
            Factory, t.TenantId, oip, t.UserId, RequestStatus.Resolved, created, handled);

        var resp = await client.GetAsync("/api/reports/blocks-per-process");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var row = doc.RootElement.GetProperty("processes").EnumerateArray()
            .Single(p => p.GetProperty("processId").GetGuid() == processId);

        // Union of [06–14] ∪ [10–18] = [06–18] = 12h. Summing would give 16h.
        row.GetProperty("averageDurationHours").GetDouble().Should().BeApproximately(12.0, 0.1);
    }

    [Fact]
    public async Task BlocksPerProcess_zero_working_hour_blocks_are_excluded_from_average()
    {
        // Two resolved blocks on one process: one spans 8 working hours, the
        // other is opened AND resolved entirely outside the shift (0 working
        // hours). The 0h block must NOT dilute the average (8h, not 4h) — but
        // both still count toward submitted/approved (Bojan 29.05.2026: "izbaciti 0").
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId, startTime: new TimeOnly(6, 0), endTime: new TimeOnly(14, 0));
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        var oip1 = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, status: ProcessStatus.InProgress);
        var oip2 = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, status: ProcessStatus.InProgress);

        // 8 working hours (06:00 → 14:00, the full shift).
        await TestDataSeeder.SeedBlockRequestAsync(
            Factory, t.TenantId, oip1, t.UserId, RequestStatus.Resolved,
            new DateTime(2026, 5, 4, 6, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 4, 14, 0, 0, DateTimeKind.Utc));
        // 0 working hours (22:00 → 23:00, entirely after the shift).
        await TestDataSeeder.SeedBlockRequestAsync(
            Factory, t.TenantId, oip2, t.UserId, RequestStatus.Resolved,
            new DateTime(2026, 5, 4, 22, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 4, 23, 0, 0, DateTimeKind.Utc));

        var resp = await client.GetAsync("/api/reports/blocks-per-process");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var row = doc.RootElement.GetProperty("processes").EnumerateArray()
            .Single(p => p.GetProperty("processId").GetGuid() == processId);

        // Average = 8h (only the non-zero block), NOT 4h = (8 + 0) / 2.
        row.GetProperty("averageDurationHours").GetDouble().Should().BeApproximately(8.0, 0.1);
        // Counts still include both blocks.
        row.GetProperty("totalSubmitted").GetInt32().Should().Be(2);
        row.GetProperty("approvedCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task BlocksPerProcess_unauthenticated_returns_401()
    {
        var anon = Factory.CreateClient();
        var resp = await anon.GetAsync("/api/reports/blocks-per-process");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
