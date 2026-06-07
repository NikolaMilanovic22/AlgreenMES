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
}
