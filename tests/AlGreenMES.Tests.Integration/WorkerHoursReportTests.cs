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
/// /api/reports/worker-hours — per-worker totals + daily breakdown. This
/// session added lazy auto-logout to this endpoint too (same helper as
/// Efikasnost), so the same cap behaviour applies here.
/// </summary>
public class WorkerHoursReportTests : IntegrationTestBase
{
    public WorkerHoursReportTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task WorkerHours_caps_closed_session_with_absurd_duration()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            maxOvertimeHours: 6);

        var checkIn = DateTime.UtcNow.Date.AddDays(-3).AddHours(6);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, checkIn, checkIn.AddDays(7));

        var from = DateOnly.FromDateTime(checkIn).AddDays(-1);
        var to = DateOnly.FromDateTime(checkIn).AddDays(1);

        var resp = await client.GetAsync(
            $"/api/reports/worker-hours?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var worker = doc.RootElement.GetProperty("workers").EnumerateArray()
            .Single(w => w.GetProperty("userId").GetGuid() == t.UserId);
        // Cap = 8h shift + 6h overtime = 14h = 840 min (NOT the raw 7 days).
        worker.GetProperty("totalWorkedMinutes").GetInt32().Should().Be(840);
    }

    [Fact]
    public async Task WorkerHours_matches_shift_by_local_time_not_utc()
    {
        // Regression for the pilot bug (Bojan 29.06.2026): CheckInTime is UTC
        // but shift times are local. A day worker checks in 06:30 Belgrade
        // (04:30 UTC, summer) and works 8h. The 06:00–14:00 shift has a 30-min
        // break, so effective must be 480−30 = 450. Pre-fix, the UTC time-of-day
        // (04:30) matched no day shift, so no break was subtracted (effective
        // stayed 480) and regular/overtime were mis-split.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId, name: "Dnevna",
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            breakMinutes: 30);

        // 2026-06-15 is summer (CEST = UTC+2): 04:30 UTC == 06:30 local.
        var checkIn = new DateTime(2026, 6, 15, 4, 30, 0, DateTimeKind.Utc);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, checkIn, checkIn.AddHours(8)); // 8h worked

        var resp = await client.GetAsync(
            "/api/reports/worker-hours?from=2026-06-15&to=2026-06-15");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var worker = doc.RootElement.GetProperty("workers").EnumerateArray()
            .Single(w => w.GetProperty("userId").GetGuid() == t.UserId);

        worker.GetProperty("totalWorkedMinutes").GetInt32().Should().Be(480);
        // Break applied → proves the shift matched on LOCAL time (06:30), not UTC (04:30).
        worker.GetProperty("effectiveMinutes").GetInt32().Should().Be(450);
    }

    [Fact]
    public async Task WorkerHours_returns_correct_total_for_legit_session()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            maxOvertimeHours: 6);

        var checkIn = DateTime.UtcNow.Date.AddDays(-1).AddHours(6);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, checkIn, checkIn.AddHours(8));  // legit 8h

        var from = DateOnly.FromDateTime(checkIn).AddDays(-1);
        var to = DateOnly.FromDateTime(checkIn).AddDays(1);

        var resp = await client.GetAsync(
            $"/api/reports/worker-hours?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var worker = doc.RootElement.GetProperty("workers").EnumerateArray()
            .Single(w => w.GetProperty("userId").GetGuid() == t.UserId);
        // 8h = 480 min, under the 14h cap → passes through unchanged.
        worker.GetProperty("totalWorkedMinutes").GetInt32().Should().Be(480);
    }

    [Fact]
    public async Task WorkerHours_daily_breakdown_carries_per_day_detail()
    {
        // Sati radnika (29.05.2026): per-worker totals + a daily row carrying
        // the rich columns (regular/overtime/effective/active/uncovered + times).
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            maxOvertimeHours: 6);

        var day1 = DateTime.UtcNow.Date.AddDays(-3).AddHours(6);
        var day2 = DateTime.UtcNow.Date.AddDays(-2).AddHours(6);
        await TestDataSeeder.SeedWorkSessionAsync(Factory, t.TenantId, t.UserId, day1, day1.AddHours(8));
        await TestDataSeeder.SeedWorkSessionAsync(Factory, t.TenantId, t.UserId, day2, day2.AddHours(8));

        var from = DateOnly.FromDateTime(day1).AddDays(-1);
        var to = DateOnly.FromDateTime(day2).AddDays(1);
        var resp = await client.GetAsync(
            $"/api/reports/worker-hours?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var worker = doc.RootElement.GetProperty("workers").EnumerateArray()
            .Single(w => w.GetProperty("userId").GetGuid() == t.UserId);
        worker.GetProperty("totalWorkedMinutes").GetInt32().Should().Be(960);

        var daily = worker.GetProperty("dailyBreakdown").EnumerateArray().ToList();
        daily.Should().HaveCount(2);
        var d = daily[0];
        d.GetProperty("totalWorkedMinutes").GetInt32().Should().Be(480);
        d.TryGetProperty("regularMinutes", out _).Should().BeTrue();
        d.TryGetProperty("overtimeMinutes", out _).Should().BeTrue();
        d.TryGetProperty("effectiveMinutes", out _).Should().BeTrue();
        d.TryGetProperty("activeMinutes", out _).Should().BeTrue();
        d.TryGetProperty("uncoveredMinutes", out _).Should().BeTrue();
        d.TryGetProperty("firstCheckIn", out _).Should().BeTrue();
        d.TryGetProperty("lastCheckOut", out _).Should().BeTrue();
    }

    [Fact]
    public async Task WorkerHours_splits_regular_and_overtime_at_shift_duration()
    {
        // A 10h session on an 8h shift (cap = 8h + 6h overtime = 14h, so the
        // session is NOT capped). Regular caps at the 8h shift duration
        // (480 min); the remaining 2h (120 min) becomes overtime. No break
        // configured → effective = total worked.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            breakMinutes: 0,
            maxOvertimeHours: 6);

        var checkIn = DateTime.UtcNow.Date.AddDays(-1).AddHours(6);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, checkIn, checkIn.AddHours(10)); // 10h

        var from = DateOnly.FromDateTime(checkIn).AddDays(-1);
        var to = DateOnly.FromDateTime(checkIn).AddDays(1);
        var resp = await client.GetAsync(
            $"/api/reports/worker-hours?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var worker = doc.RootElement.GetProperty("workers").EnumerateArray()
            .Single(w => w.GetProperty("userId").GetGuid() == t.UserId);
        worker.GetProperty("totalWorkedMinutes").GetInt32().Should().Be(600);
        worker.GetProperty("regularMinutes").GetInt32().Should().Be(480);
        worker.GetProperty("overtimeMinutes").GetInt32().Should().Be(120);
        // No break → effective = worked.
        worker.GetProperty("effectiveMinutes").GetInt32().Should().Be(600);
    }

    [Fact]
    public async Task WorkerHours_excludes_non_department_users()
    {
        // Worker reports are for factory-floor (Department) staff only. An Admin
        // with a check-in session must NOT appear; a Department worker must.
        // (Confirmed by Milos 29.05.2026: only the worker role belongs here.)
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin); // admin, for auth
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId, startTime: new TimeOnly(6, 0), endTime: new TimeOnly(14, 0));

        var worker = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Department);
        var day = DateTime.UtcNow.Date.AddDays(-1).AddHours(6);
        await TestDataSeeder.SeedWorkSessionAsync(Factory, t.TenantId, t.UserId, day, day.AddHours(8)); // admin session
        await TestDataSeeder.SeedWorkSessionAsync(Factory, t.TenantId, worker, day, day.AddHours(8));    // worker session

        var from = DateOnly.FromDateTime(day).AddDays(-1);
        var to = DateOnly.FromDateTime(day).AddDays(1);
        var resp = await client.GetAsync(
            $"/api/reports/worker-hours?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var ids = doc.RootElement.GetProperty("workers").EnumerateArray()
            .Select(w => w.GetProperty("userId").GetGuid()).ToList();
        ids.Should().Contain(worker);       // Department worker present
        ids.Should().NotContain(t.UserId);  // Admin excluded
    }

    [Fact]
    public async Task WorkerHours_per_worker_overtime_excludes_trivial_daily_amounts()
    {
        // Per-worker OT total excludes per-day OT ≤ 30 min (Excel v2 E35 SUMIF
        // ">0.5h"). Two days: day1 has 25 min OT (excluded), day2 has 60 min OT
        // (included). Worker total OT = 60 min, NOT 85 min. Per-day OT stays
        // raw in the daily breakdown.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            maxOvertimeHours: 6);

        var day1 = DateTime.UtcNow.Date.AddDays(-3).AddHours(6);
        var day2 = DateTime.UtcNow.Date.AddDays(-2).AddHours(6);
        // Day 1: 8h25m → 480 regular + 25 OT (trivial).
        await TestDataSeeder.SeedWorkSessionAsync(Factory, t.TenantId, t.UserId, day1, day1.AddMinutes(505));
        // Day 2: 9h → 480 regular + 60 OT (real).
        await TestDataSeeder.SeedWorkSessionAsync(Factory, t.TenantId, t.UserId, day2, day2.AddHours(9));

        var from = DateOnly.FromDateTime(day1).AddDays(-1);
        var to = DateOnly.FromDateTime(day2).AddDays(1);
        var resp = await client.GetAsync(
            $"/api/reports/worker-hours?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var worker = doc.RootElement.GetProperty("workers").EnumerateArray()
            .Single(w => w.GetProperty("userId").GetGuid() == t.UserId);
        worker.GetProperty("regularMinutes").GetInt32().Should().Be(960);     // 480 + 480
        worker.GetProperty("overtimeMinutes").GetInt32().Should().Be(60);     // ONLY day 2 (60 > 30)
        worker.GetProperty("totalWorkedMinutes").GetInt32().Should().Be(1045); // raw 505 + 540

        // Daily breakdown still shows per-day OT unfiltered (Excel rows 15-34).
        var daily = worker.GetProperty("dailyBreakdown").EnumerateArray()
            .OrderBy(d => d.GetProperty("date").GetString())
            .ToList();
        daily.Should().HaveCount(2);
        daily[0].GetProperty("overtimeMinutes").GetInt32().Should().Be(25);
        daily[1].GetProperty("overtimeMinutes").GetInt32().Should().Be(60);
    }

    [Fact]
    public async Task WorkerHours_autoLogoutApplied_derived_from_persisted_was_auto_closed_flag()
    {
        // Bojan 03.06.2026 — the per-day autoLogoutApplied flag is derived
        // from the persisted WorkSession.WasAutoClosed (truth-from-storage),
        // NOT from a totalWorked-vs-cap comparison (the old comparison was
        // off-by-one when the system auto-closed exactly at the cap).
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            maxOvertimeHours: 6,
            autoLogoutRegularMinutes: 510); // 8.5h

        var day = DateTime.UtcNow.Date.AddDays(-1).AddHours(6);
        // Session closed EXACTLY at the cap (totalWorked == cap). The old
        // strict-greater comparison missed this; the new flag derivation reads
        // WasAutoClosed=true and reports DA ⚠ correctly.
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, day, day.AddMinutes(510), wasAutoClosed: true);

        var from = DateOnly.FromDateTime(day).AddDays(-1);
        var to = DateOnly.FromDateTime(day).AddDays(1);
        var resp = await client.GetAsync(
            $"/api/reports/worker-hours?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var worker = doc.RootElement.GetProperty("workers").EnumerateArray()
            .Single(w => w.GetProperty("userId").GetGuid() == t.UserId);
        var daily = worker.GetProperty("dailyBreakdown").EnumerateArray().Single();
        daily.GetProperty("autoLogoutApplied").GetBoolean().Should().BeTrue();
    }

    // NOTE (Bojan 03.06.2026 — open-log active fix): tried several approaches
    // to write an integration test for this — all of them hit a
    // DbUpdateConcurrencyException on the Process+SubProcess save (line 589 in
    // the prior seeder shape). The pre-existing SeedSubProcessLogAsync hits
    // the same issue. Suspect an EF 9 + Npgsql + interceptor interaction
    // around child-collection adds, but it doesn't reproduce in other tests.
    // The production fix itself is small and well-contained in
    // ReportingQueryService — open logs are included in the active union with
    // EndTime clipped to the session's checkout. Verified by post-deploy
    // smoke test against Milojica's session (was Aktivno=0, now reports
    // actual active time).

    [Fact]
    public async Task WorkerHours_active_includes_process_level_work_with_no_subprocesses()
    {
        // Bojan 04.06.2026 (Bug B) — Petar started Krojenje (a process with no
        // sub-processes) and worked 8h, but Aktivno showed 0 because the
        // calculation only summed sub-process logs (which carry user_id).
        // Process-level work is now attributed via OrderItemProcess.
        // StartedByUserId — set at StartProcessWork and counted as an
        // active interval (StartedAt → PausedAt) when the process has no
        // non-withdrawn sub-processes.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            maxOvertimeHours: 6);

        var day = DateTime.UtcNow.Date.AddDays(-1).AddHours(6);
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, day, day.AddHours(8));

        // Krojenje-like process with no sub-processes. Seed an
        // OrderItemProcessLog covering the full session (since 06.06 the
        // report reads from logs, not from OIP.StartedAt/PausedAt directly).
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.InProgress);
        await SeedProcessLogAsync(t.TenantId, oipId, t.UserId, day, day.AddHours(8));

        var from = DateOnly.FromDateTime(day).AddDays(-1);
        var to = DateOnly.FromDateTime(day).AddDays(1);
        var resp = await client.GetAsync(
            $"/api/reports/worker-hours?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var worker = doc.RootElement.GetProperty("workers").EnumerateArray()
            .Single(w => w.GetProperty("userId").GetGuid() == t.UserId);
        var daily = worker.GetProperty("dailyBreakdown").EnumerateArray().Single();
        daily.GetProperty("activeMinutes").GetInt32().Should().Be(480);
    }

    /// <summary>Helper: seed an OrderItemProcessLog with explicit timestamps.</summary>
    private async Task SeedProcessLogAsync(Guid tenantId, Guid oipId, Guid userId, DateTime start, DateTime? end)
    {
        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await ordersDb.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO orders.order_item_process_logs
                (id, order_item_process_id, user_id, tenant_id, start_time, end_time, duration_seconds, created_at)
            VALUES
                ({Guid.NewGuid()}, {oipId}, {userId}, {tenantId},
                 {start}, {end},
                 {(end.HasValue ? (int?)(end.Value - start).TotalSeconds : null)},
                 NOW())");
    }

    [Fact]
    public async Task WorkerHours_active_uses_totalWorked_not_effective_so_break_does_not_eat_overtime()
    {
        // Bojan 04.06.2026 (Bug C) — Milojica's actual sub-process work was
        // 8h12m but the report showed Aktivno=8h00m. The previous formula
        // capped active at `effective` (= totalWorked − breakMinutes), so
        // the 30-min break ate the overtime work into Nepokriveno. Aktivno
        // is RAW active time within the session — break is for Efektivno.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            breakMinutes: 30,    // ← the break that USED TO clip active
            maxOvertimeHours: 6);

        var day = DateTime.UtcNow.Date.AddDays(-1).AddHours(6);
        // 8h30m session: 8h regular + 30m overtime.
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, day, day.AddMinutes(510));

        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.InProgress);
        await SeedProcessLogAsync(t.TenantId, oipId, t.UserId, day, day.AddMinutes(510));

        var from = DateOnly.FromDateTime(day).AddDays(-1);
        var to = DateOnly.FromDateTime(day).AddDays(1);
        var resp = await client.GetAsync(
            $"/api/reports/worker-hours?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var worker = doc.RootElement.GetProperty("workers").EnumerateArray()
            .Single(w => w.GetProperty("userId").GetGuid() == t.UserId);
        var daily = worker.GetProperty("dailyBreakdown").EnumerateArray().Single();
        // The process active window equals the full session: 510 min. Active
        // must NOT be capped at effective (480) — that was the Bug C bug.
        daily.GetProperty("activeMinutes").GetInt32().Should().Be(510);
        // Uncovered must use totalWorked − active (not effective − active).
        daily.GetProperty("uncoveredMinutes").GetInt32().Should().Be(0);
        // Efektivno still subtracts break (it's a separate column).
        daily.GetProperty("effectiveMinutes").GetInt32().Should().Be(480);
    }

    [Fact]
    public async Task WorkerHours_active_clips_process_level_paused_at_to_session_checkout()
    {
        // Bojan 04.06.2026 (Bug C, half 2) — closed work intervals that ran
        // past the (backdated) auto-checkout were counted as active beyond
        // the session. Milojica's sub-process log ended ~2 min after the
        // session was backdated-closed by AutoCheckOut. Active intervals
        // (both subprocess logs and process-level windows) are now clipped
        // to clipUpper (session checkout) — using a process-level interval
        // here to avoid the SeedSubProcessLog seeder quirk.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            maxOvertimeHours: 6);

        var day = DateTime.UtcNow.Date.AddDays(-1).AddHours(6);
        // 8h session: check-in at day, check-out at day+8h.
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, day, day.AddHours(8));

        // Process log "ended" 5 min PAST the session checkout (simulating
        // the BG service running log.End() after the backdated session
        // checkout). The report should clip the interval at the session
        // checkout, not include the 5-min overshoot.
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.InProgress);
        await SeedProcessLogAsync(t.TenantId, oipId, t.UserId, day, day.AddHours(8).AddMinutes(5));

        var from = DateOnly.FromDateTime(day).AddDays(-1);
        var to = DateOnly.FromDateTime(day).AddDays(1);
        var resp = await client.GetAsync(
            $"/api/reports/worker-hours?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var worker = doc.RootElement.GetProperty("workers").EnumerateArray()
            .Single(w => w.GetProperty("userId").GetGuid() == t.UserId);
        var daily = worker.GetProperty("dailyBreakdown").EnumerateArray().Single();
        // Clipped at session checkout — 480 min, NOT 485.
        daily.GetProperty("activeMinutes").GetInt32().Should().Be(480);
    }

    [Fact]
    public async Task WorkerHours_active_excludes_paused_periods_between_logs()
    {
        // Bojan/Sale 06.06.2026 (Bug D) — process-level work was treated as
        // (StartedAt → PausedAt ?? CompletedAt) which counted offline gaps
        // (e.g. auto-logout → relogin) AND in-session manual pauses as
        // continuously active. After the log-table fix each Start/Resume
        // creates a new log and each Pause/Complete ends one; the report
        // unions only the open log intervals. A 1-hour mid-day gap between
        // two work periods must NOT count as active.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            startTime: new TimeOnly(6, 0),
            endTime: new TimeOnly(14, 0),
            maxOvertimeHours: 6);

        var day = DateTime.UtcNow.Date.AddDays(-1).AddHours(6);
        // Single 9h session that covers both work periods + the gap.
        await TestDataSeeder.SeedWorkSessionAsync(
            Factory, t.TenantId, t.UserId, day, day.AddHours(9));

        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.InProgress);

        // Two work periods with a 1h gap in between:
        //   [day → day+4h] : 4h active
        //   [day+5h → day+9h] : 4h active
        // Gap [day+4h → day+5h] : NOT active (worker paused / went to lunch).
        await SeedProcessLogAsync(t.TenantId, oipId, t.UserId, day, day.AddHours(4));
        await SeedProcessLogAsync(t.TenantId, oipId, t.UserId, day.AddHours(5), day.AddHours(9));

        var from = DateOnly.FromDateTime(day).AddDays(-1);
        var to = DateOnly.FromDateTime(day).AddDays(1);
        var resp = await client.GetAsync(
            $"/api/reports/worker-hours?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var worker = doc.RootElement.GetProperty("workers").EnumerateArray()
            .Single(w => w.GetProperty("userId").GetGuid() == t.UserId);
        var daily = worker.GetProperty("dailyBreakdown").EnumerateArray().Single();
        // 8h active (two 4h periods unioned). NOT 9h — the 1h gap is excluded.
        daily.GetProperty("activeMinutes").GetInt32().Should().Be(480);
        // Uncovered = totalWorked (540) − active (480) = 60 min — i.e. the gap.
        daily.GetProperty("uncoveredMinutes").GetInt32().Should().Be(60);
    }
}
