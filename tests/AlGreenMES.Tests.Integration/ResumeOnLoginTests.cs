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
/// ResumeOnLoginCommand — fires when a worker logs in on the tablet. Bug
/// 07.06.2026: was auto-resuming ALL paused-on-logout OIPs of the worker's
/// qualified processes, including ones paused by OTHER workers. New
/// behaviour: only auto-resume work where the most-recent log's UserId
/// matches the logging-in worker.
/// </summary>
public class ResumeOnLoginTests : IntegrationTestBase
{
    public ResumeOnLoginTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task ResumeOnLogin_skips_OIP_last_worked_by_a_different_user()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department); // worker A
        var workerB = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Department);

        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.InProgress);

        // Worker B paused this OIP (open log with UserId=workerB + PausedOnLogoutAt set).
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var now = DateTime.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO orders.order_item_process_logs
                    (id, order_item_process_id, user_id, tenant_id, start_time, end_time, duration_seconds, created_at)
                VALUES ({Guid.NewGuid()}, {oipId}, {workerB}, {t.TenantId},
                        {now.AddHours(-1)}, {now.AddMinutes(-30)}, 1800, NOW())");
            await db.OrderItemProcesses
                .IgnoreQueryFilters()
                .Where(p => p.Id == oipId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.PausedAt, (DateTime?)now.AddMinutes(-30))
                    .SetProperty(p => p.PausedOnLogoutAt, (DateTime?)now.AddMinutes(-30))
                    .SetProperty(p => p.StartedByUserId, (Guid?)workerB));
        }

        // Worker A logs in and ResumeOnLogin fires for the process. Going
        // through the HTTP endpoint so the tenant claim is on the request
        // — calling the mediator directly would hit the OrdersDbContext
        // tenant filter with Guid.Empty and return zero processes.
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var resumeResp = await client.PostAsJsonAsync(
            "/api/order-item-processes/resume-on-login",
            new { processId, userId = t.UserId });
        resumeResp.IsSuccessStatusCode.Should().BeTrue();

        // Verify: worker B's OIP stayed paused — no new log was opened.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var openLogsForUserA = await db.OrderItemProcessLogs
                .IgnoreQueryFilters()
                .CountAsync(l => l.OrderItemProcessId == oipId && l.UserId == t.UserId && l.EndTime == null);
            openLogsForUserA.Should().Be(0);

            var oip = await db.OrderItemProcesses
                .IgnoreQueryFilters()
                .SingleAsync(p => p.Id == oipId);
            oip.PausedOnLogoutAt.Should().NotBeNull(); // still flagged as paused-on-logout
        }
    }

    [Fact]
    public async Task ResumeOnLogin_resumes_OIP_last_worked_by_same_user()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);

        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.InProgress);

        // This worker paused their own OIP earlier (closed log).
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var now = DateTime.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO orders.order_item_process_logs
                    (id, order_item_process_id, user_id, tenant_id, start_time, end_time, duration_seconds, created_at)
                VALUES ({Guid.NewGuid()}, {oipId}, {t.UserId}, {t.TenantId},
                        {now.AddHours(-1)}, {now.AddMinutes(-30)}, 1800, NOW())");
            await db.OrderItemProcesses
                .IgnoreQueryFilters()
                .Where(p => p.Id == oipId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.PausedAt, (DateTime?)now.AddMinutes(-30))
                    .SetProperty(p => p.PausedOnLogoutAt, (DateTime?)now.AddMinutes(-30))
                    .SetProperty(p => p.StartedByUserId, (Guid?)t.UserId));
        }

        // Same worker logs back in.
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var resumeResp = await client.PostAsJsonAsync(
            "/api/order-item-processes/resume-on-login",
            new { processId, userId = t.UserId });
        resumeResp.IsSuccessStatusCode.Should().BeTrue();

        // Verify: a new open log was created for this user (auto-resume fired).
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var openLogsForUser = await db.OrderItemProcessLogs
                .IgnoreQueryFilters()
                .CountAsync(l => l.OrderItemProcessId == oipId && l.UserId == t.UserId && l.EndTime == null);
            openLogsForUser.Should().Be(1);
        }
    }

    /// <summary>
    /// Saša 08.06.2026 Bug 2 — parent-marker variant. The fix also looks
    /// at process.PausedOnLogoutAt (not just sub.PausedOnLogoutAt) so the
    /// auto-resume works when only the parent marker survives the auto-
    /// logout chain. Sub-process here has NO PausedOnLogoutAt of its own
    /// — only the parent OIP carries the marker.
    /// </summary>
    [Fact]
    public async Task ResumeOnLogin_resumes_active_subprocess_when_only_parent_marker_set()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);

        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.InProgress);

        Guid subProcessId = Guid.NewGuid();
        Guid oispId = Guid.NewGuid();
        using (var scope = Factory.Services.CreateScope())
        {
            var productionDb = scope.ServiceProvider.GetRequiredService<AlGreenMES.Modules.Production.Infrastructure.Persistence.ProductionDbContext>();
            await productionDb.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO production.sub_processes
                    (id, process_id, tenant_id, name, sequence_order, is_active, created_at)
                VALUES ({subProcessId}, {processId}, {t.TenantId},
                        {"SP-Parent"}, 1, true, NOW())");

            var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var now = DateTime.UtcNow;
            // sub.PausedOnLogoutAt = null (the old sub-level marker missing),
            // but parent.PausedOnLogoutAt = set (new parent-level marker).
            await ordersDb.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO orders.order_item_sub_processes
                    (id, order_item_process_id, sub_process_id, tenant_id, status,
                     total_duration_minutes, is_withdrawn, created_at)
                VALUES ({oispId}, {oipId}, {subProcessId}, {t.TenantId}, {(int)SubProcessStatus.InProgress},
                        30, false, NOW())");
            await ordersDb.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO orders.order_item_sub_process_logs
                    (id, order_item_sub_process_id, user_id, tenant_id, start_time, end_time, duration_minutes, created_at)
                VALUES ({Guid.NewGuid()}, {oispId}, {t.UserId}, {t.TenantId},
                        {now.AddHours(-1)}, {now.AddMinutes(-30)}, 30, NOW())");
            await ordersDb.OrderItemProcesses
                .IgnoreQueryFilters()
                .Where(p => p.Id == oipId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.PausedOnLogoutAt, (DateTime?)now.AddMinutes(-30)));
        }

        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var resumeResp = await client.PostAsJsonAsync(
            "/api/order-item-processes/resume-on-login",
            new { processId, userId = t.UserId });
        resumeResp.IsSuccessStatusCode.Should().BeTrue();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var openLogs = await db.OrderItemSubProcessLogs
                .IgnoreQueryFilters()
                .CountAsync(l => l.OrderItemSubProcessId == oispId && l.UserId == t.UserId && l.EndTime == null);
            openLogs.Should().Be(1);

            var oip = await db.OrderItemProcesses
                .IgnoreQueryFilters()
                .SingleAsync(p => p.Id == oipId);
            oip.PausedOnLogoutAt.Should().BeNull(); // parent marker cleared
        }
    }

    /// <summary>
    /// Saša 08.06.2026 Bug 2: after auto-logout, OT relogin only resumed
    /// processes WITHOUT sub-processes. Started sub-processes stayed
    /// paused. Reproduces the sub-process branch of ResumeOnLogin.
    /// </summary>
    [Fact]
    public async Task ResumeOnLogin_resumes_active_subprocess_after_auto_logout()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);

        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, ProcessStatus.InProgress);

        Guid subProcessId = Guid.NewGuid();
        Guid oispId = Guid.NewGuid();
        using (var scope = Factory.Services.CreateScope())
        {
            // Bypass EF entity APIs (DbUpdateConcurrencyException on
            // Process.AddSubProcess save — known test-infra issue noted in
            // WorkerHoursReportTests). Direct SQL gets us the same row state.
            var productionDb = scope.ServiceProvider.GetRequiredService<AlGreenMES.Modules.Production.Infrastructure.Persistence.ProductionDbContext>();
            await productionDb.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO production.sub_processes
                    (id, process_id, tenant_id, name, sequence_order, is_active, created_at)
                VALUES ({subProcessId}, {processId}, {t.TenantId},
                        {"SP-Test"}, 1, true, NOW())");

            var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var now = DateTime.UtcNow;
            // Post-auto-logout state: OISP InProgress + PausedOnLogoutAt set
            // + one closed log by this user.
            await ordersDb.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO orders.order_item_sub_processes
                    (id, order_item_process_id, sub_process_id, tenant_id, status,
                     total_duration_minutes, is_withdrawn, paused_on_logout_at, created_at)
                VALUES ({oispId}, {oipId}, {subProcessId}, {t.TenantId}, {(int)SubProcessStatus.InProgress},
                        30, false, {now.AddMinutes(-30)}, NOW())");
            await ordersDb.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO orders.order_item_sub_process_logs
                    (id, order_item_sub_process_id, user_id, tenant_id, start_time, end_time, duration_minutes, created_at)
                VALUES ({Guid.NewGuid()}, {oispId}, {t.UserId}, {t.TenantId},
                        {now.AddHours(-1)}, {now.AddMinutes(-30)}, 30, NOW())");
        }

        // OT relogin fires ResumeOnLogin.
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var resumeResp = await client.PostAsJsonAsync(
            "/api/order-item-processes/resume-on-login",
            new { processId, userId = t.UserId });
        resumeResp.IsSuccessStatusCode.Should().BeTrue();

        // Verify: a new open log was created on the sub-process and the
        // PausedOnLogoutAt flag was cleared.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var openLogs = await db.OrderItemSubProcessLogs
                .IgnoreQueryFilters()
                .CountAsync(l => l.OrderItemSubProcessId == oispId && l.UserId == t.UserId && l.EndTime == null);
            openLogs.Should().Be(1);

            var oisp = await db.OrderItemSubProcesses
                .IgnoreQueryFilters()
                .SingleAsync(sp => sp.Id == oispId);
            oisp.PausedOnLogoutAt.Should().BeNull();
        }
    }
}
