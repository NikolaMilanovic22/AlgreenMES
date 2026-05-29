using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlGreenMES.Modules.Identity.Application.Services;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using AlGreenMES.Modules.Production.Domain.Entities;
using AlGreenMES.Modules.Production.Domain.Enums;
using AlGreenMES.Modules.Production.Infrastructure.Persistence;
using AlGreenMES.Modules.Tenancy.Domain.Entities;
using AlGreenMES.Modules.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlGreenMES.Tests.Integration.Helpers;

public static class TestDataSeeder
{
    public const string DefaultPassword = "TestPass123!";

    public static async Task<SeededTenant> SeedTenantWithUserAsync(
        AlgreenWebApplicationFactory factory,
        UserRole role = UserRole.Admin,
        string? tenantCodeOverride = null,
        string? emailOverride = null)
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var tenancyDb = sp.GetRequiredService<TenancyDbContext>();
        var identityDb = sp.GetRequiredService<IdentityDbContext>();
        var passwordHasher = sp.GetRequiredService<IPasswordHasher>();

        var code = tenantCodeOverride ?? $"T{Guid.NewGuid():N}".Substring(0, 8).ToUpperInvariant();
        var email = emailOverride ?? $"u-{Guid.NewGuid():N}".Substring(0, 10) + "@test.local";

        var tenant = Tenant.Create($"Tenant {code}", code);
        tenancyDb.Tenants.Add(tenant);
        await tenancyDb.SaveChangesAsync();

        var user = User.Create(tenant.Id, email, passwordHasher.HashPassword(DefaultPassword),
            "Test", "User", role);
        identityDb.Users.Add(user);
        await identityDb.SaveChangesAsync();

        return new SeededTenant(tenant.Id, tenant.Code, user.Id, email, DefaultPassword, role);
    }

    public static async Task<string> LoginAndGetTokenAsync(
        HttpClient client, string email, string password, string tenantCode)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password,
            TenantCode = tenantCode
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        if (body is null || string.IsNullOrEmpty(body.Token))
            throw new InvalidOperationException("Login returned no token");
        return body.Token;
    }

    public static async Task<HttpClient> AuthenticatedClientAsync(
        AlgreenWebApplicationFactory factory, SeededTenant t)
    {
        var client = factory.CreateClient();
        var token = await LoginAndGetTokenAsync(client, t.Email, t.Password, t.TenantCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<(SeededTenant tenantA, SeededTenant tenantB)> SeedTwoTenantsAsync(
        AlgreenWebApplicationFactory factory,
        UserRole roleForA = UserRole.Admin,
        UserRole roleForB = UserRole.Admin)
    {
        var a = await SeedTenantWithUserAsync(factory, roleForA);
        var b = await SeedTenantWithUserAsync(factory, roleForB);
        return (a, b);
    }

    public static async Task<Guid> SeedAdditionalUserAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        UserRole role = UserRole.Admin)
    {
        var (id, _, _) = await SeedAdditionalUserWithCredsAsync(factory, tenantId, role);
        return id;
    }

    public static async Task<(Guid UserId, string Email, string Password)> SeedAdditionalUserWithCredsAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        UserRole role = UserRole.Admin)
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var identityDb = sp.GetRequiredService<IdentityDbContext>();
        var passwordHasher = sp.GetRequiredService<IPasswordHasher>();

        var email = $"u-{Guid.NewGuid():N}".Substring(0, 10) + "@test.local";
        var user = User.Create(tenantId, email, passwordHasher.HashPassword(DefaultPassword),
            "Test", "User", role);
        identityDb.Users.Add(user);
        await identityDb.SaveChangesAsync();
        return (user.Id, email, DefaultPassword);
    }

    public static async Task<Guid> SeedOrderAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        Guid createdByUserId,
        string? orderNumber = null)
    {
        using var scope = factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var number = orderNumber ?? $"ORD-{Guid.NewGuid():N}".Substring(0, 16);
        var order = Order.Create(
            tenantId,
            number,
            DateTime.UtcNow.AddDays(7),
            priority: 3,
            OrderType.Standard,
            createdByUserId,
            notes: null);

        ordersDb.Orders.Add(order);
        await ordersDb.SaveChangesAsync();
        return order.Id;
    }

    public static async Task<Guid> SeedProcessAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        Guid? createdByUserId = null,
        string? processName = null)
    {
        using var scope = factory.Services.CreateScope();
        var productionDb = scope.ServiceProvider.GetRequiredService<ProductionDbContext>();

        var name = processName ?? $"Proc-{Guid.NewGuid():N}".Substring(0, 12);
        var code = $"P{Guid.NewGuid():N}".Substring(0, 6).ToUpperInvariant();
        var process = Process.Create(tenantId, code, name, sequenceOrder: 1, createdByUserId);

        productionDb.Processes.Add(process);
        await productionDb.SaveChangesAsync();
        return process.Id;
    }

    /// <summary>
    /// Seeds a minimal Order → Item → OIP chain and returns the OIP id.
    /// Used by /reports tests to exercise PATCH excluded-from-reports etc.
    /// </summary>
    public static async Task<Guid> SeedOrderItemProcessAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        Guid createdByUserId,
        Guid processId,
        Guid productCategoryId,
        ProcessStatus? status = null,
        int? totalDurationSeconds = null,
        ComplexityType? complexity = null)
    {
        using var scope = factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var order = Order.Create(
            tenantId,
            $"ORD-{Guid.NewGuid():N}".Substring(0, 16),
            DateTime.UtcNow.AddDays(7),
            priority: 3,
            OrderType.Standard,
            createdByUserId,
            notes: null);
        var item = order.AddItem(productCategoryId, productName: null, quantity: 1);
        var oip = item.AddProcess(processId, complexity);

        if (status.HasValue && status.Value != ProcessStatus.Pending)
        {
            oip.Start();
            if (totalDurationSeconds is { } secs && secs > 0)
                oip.AddDuration(secs);
            switch (status.Value)
            {
                case ProcessStatus.Completed:
                    oip.Complete();
                    break;
                case ProcessStatus.Blocked:
                    oip.Block(createdByUserId, "test");
                    break;
                // InProgress: leave as-is (Start() already transitioned it).
            }
        }

        ordersDb.Orders.Add(order);
        await ordersDb.SaveChangesAsync();
        return oip.Id;
    }

    public static async Task<Guid> SeedProductCategoryAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        Guid? createdByUserId = null)
    {
        using var scope = factory.Services.CreateScope();
        var productionDb = scope.ServiceProvider.GetRequiredService<ProductionDbContext>();
        var pc = ProductCategory.Create(
            tenantId,
            $"Cat-{Guid.NewGuid():N}".Substring(0, 12),
            description: null,
            createdByUserId);
        productionDb.ProductCategories.Add(pc);
        await productionDb.SaveChangesAsync();
        return pc.Id;
    }

    /// <summary>
    /// Adds processes (in given order) + dependencies to an existing product
    /// category. Dependencies are pairs (processId, dependsOnProcessId).
    /// Used by funnel "ready" tests so a Pending OIP can be evaluated against
    /// completed/withdrawn/pending predecessors.
    /// </summary>
    public static async Task SeedCategoryProcessesAndDepsAsync(
        AlgreenWebApplicationFactory factory,
        Guid categoryId,
        IEnumerable<Guid> processIds,
        IEnumerable<(Guid ProcessId, Guid DependsOnProcessId)>? dependencies = null)
    {
        using var scope = factory.Services.CreateScope();
        var productionDb = scope.ServiceProvider.GetRequiredService<ProductionDbContext>();
        // IgnoreQueryFilters because the seeder runs in a test scope with no
        // HTTP context — ICurrentUserService.GetCurrentTenantId() returns
        // Guid.Empty (intentional fail-closed default), which would zero
        // out the tenant query filter and return no rows.
        var category = await productionDb.ProductCategories
            .IgnoreQueryFilters()
            .Include(c => c.Processes)
            .Include(c => c.Dependencies)
            .SingleAsync(c => c.Id == categoryId);

        var order = 1;
        foreach (var pid in processIds)
            category.AddProcess(pid, sequenceOrder: order++);
        if (dependencies is not null)
        {
            foreach (var (pid, depPid) in dependencies)
                category.AddDependency(pid, depPid);
        }
        await productionDb.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds an order with multiple processes on a single item — needed for
    /// dep-chain tests (e.g., "predkrojenje must complete before staklo
    /// can start"). Each processStatus[i] sets the status of the OIP for
    /// processes[i].
    /// </summary>
    public static async Task<(Guid OrderId, Guid ItemId, Guid[] OipIds)> SeedOrderWithProcessesAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        Guid createdByUserId,
        Guid productCategoryId,
        IReadOnlyList<Guid> processIds,
        IReadOnlyList<ProcessStatus> processStatuses,
        DateTime? deliveryDate = null,
        DateTime? completedAtOverride = null)
    {
        if (processIds.Count != processStatuses.Count)
            throw new ArgumentException("processIds and processStatuses must match length");

        using var scope = factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var order = Order.Create(
            tenantId,
            $"ORD-{Guid.NewGuid():N}".Substring(0, 16),
            deliveryDate ?? DateTime.UtcNow.AddDays(7),
            priority: 3,
            OrderType.Standard,
            createdByUserId,
            notes: null);
        var item = order.AddItem(productCategoryId, productName: null, quantity: 1);
        var oips = new Guid[processIds.Count];
        for (var i = 0; i < processIds.Count; i++)
        {
            var oip = item.AddProcess(processIds[i], complexity: null);
            switch (processStatuses[i])
            {
                case ProcessStatus.Completed:
                    oip.Start();
                    oip.Complete();
                    break;
                case ProcessStatus.InProgress:
                    oip.Start();
                    break;
                case ProcessStatus.Blocked:
                    oip.Start();
                    oip.Block(createdByUserId, "test");
                    break;
                case ProcessStatus.Pending:
                    // nothing — Pending is the default
                    break;
            }
            oips[i] = oip.Id;
        }
        ordersDb.Orders.Add(order);
        await ordersDb.SaveChangesAsync();

        // Optional CompletedAt override: useful for delivery-compliance tests
        // where we need a specific completion date relative to deliveryDate.
        if (completedAtOverride.HasValue)
        {
            await ordersDb.Orders
                .IgnoreQueryFilters()
                .Where(o => o.Id == order.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(o => o.CompletedAt, completedAtOverride.Value));
        }

        return (order.Id, item.Id, oips);
    }

    /// <summary>
    /// Seeds one order with multiple items (each with a single completed
    /// process). Used to assert the manufacturing-time report emits one row
    /// PER ITEM (29.05.2026 reshape), not one row per order. Caller must still
    /// MarkOrderCompletedAsync for the order to qualify for the report.
    /// </summary>
    public static async Task<(Guid OrderId, Guid[] ItemIds)> SeedMultiItemOrderAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        Guid createdByUserId,
        Guid productCategoryId,
        Guid processId,
        int itemCount)
    {
        using var scope = factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var order = Order.Create(
            tenantId,
            $"ORD-{Guid.NewGuid():N}".Substring(0, 16),
            DateTime.UtcNow.AddDays(7),
            priority: 3,
            OrderType.Standard,
            createdByUserId,
            notes: null);

        var itemIds = new Guid[itemCount];
        for (var i = 0; i < itemCount; i++)
        {
            var item = order.AddItem(productCategoryId, productName: null, quantity: 1);
            var oip = item.AddProcess(processId, complexity: null);
            oip.Start();
            oip.Complete();
            itemIds[i] = item.Id;
        }

        ordersDb.Orders.Add(order);
        await ordersDb.SaveChangesAsync();
        return (order.Id, itemIds);
    }

    /// <summary>
    /// Marks an existing order as Completed with a given CompletedAt. Used
    /// by /reports/product-manufacturing-time tests where the report only
    /// considers orders with Status=Completed. (SeedOrderWithProcessesAsync
    /// completes the OIPs but doesn't touch the order's status.)
    /// </summary>
    public static async Task MarkOrderCompletedAsync(
        AlgreenWebApplicationFactory factory,
        Guid orderId,
        DateTime? completedAt = null)
    {
        using var scope = factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await ordersDb.Orders.IgnoreQueryFilters()
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(o => o.Status, OrderStatus.Completed)
                .SetProperty(o => o.CompletedAt, completedAt ?? DateTime.UtcNow));
    }

    /// <summary>
    /// Seeds a Shift with per-shift time-tracking config (Bojan spec 25.05.2026).
    /// Defaults match the BE seeder: 0 min break, 6h max overtime, auto-logout
    /// every 2h, 5 min alarm. Override individual params to exercise edge cases
    /// (cross-midnight shifts, different overtime caps, etc.).
    /// </summary>
    public static async Task<Guid> SeedShiftAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        string? name = null,
        TimeOnly? startTime = null,
        TimeOnly? endTime = null,
        int breakMinutes = 0,
        int maxOvertimeHours = 6,
        int autoLogoutAfterHours = 2,
        int alarmBeforeLogoutMinutes = 5)
    {
        using var scope = factory.Services.CreateScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var shift = Shift.Create(
            tenantId,
            name ?? $"Shift-{Guid.NewGuid():N}".Substring(0, 10),
            startTime ?? new TimeOnly(6, 0),
            endTime ?? new TimeOnly(14, 0),
            breakMinutes,
            maxOvertimeHours,
            autoLogoutAfterHours,
            alarmBeforeLogoutMinutes);
        identityDb.Shifts.Add(shift);
        await identityDb.SaveChangesAsync();
        return shift.Id;
    }

    /// <summary>
    /// Seeds a WorkSession with arbitrary historical timestamps. The entity's
    /// CheckIn factory hardcodes CheckInTime = UtcNow, so we override via
    /// ExecuteUpdateAsync after insert. Pass checkOutTime = null to seed an
    /// open session (for lazy auto-logout tests).
    /// </summary>
    public static async Task<Guid> SeedWorkSessionAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        Guid userId,
        DateTime checkInTime,
        DateTime? checkOutTime = null)
    {
        using var scope = factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var session = WorkSession.CheckIn(tenantId, userId);
        if (checkOutTime.HasValue)
            session.CheckOut();
        ordersDb.WorkSessions.Add(session);
        await ordersDb.SaveChangesAsync();

        // Override the auto-generated UtcNow stamps with the requested times.
        var date = DateOnly.FromDateTime(checkInTime);
        var duration = checkOutTime.HasValue
            ? (int?)Math.Max(0, (int)(checkOutTime.Value - checkInTime).TotalMinutes)
            : null;
        await ordersDb.WorkSessions
            .IgnoreQueryFilters()
            .Where(ws => ws.Id == session.Id)
            .ExecuteUpdateAsync(set => set
                .SetProperty(ws => ws.CheckInTime, checkInTime)
                .SetProperty(ws => ws.CheckOutTime, checkOutTime)
                .SetProperty(ws => ws.DurationMinutes, duration)
                .SetProperty(ws => ws.Date, date));

        return session.Id;
    }

    /// <summary>
    /// Seeds a BlockRequest against an existing OIP, with optional historical
    /// CreatedAt + HandledAt timestamps. Used by /reports/blocks-per-process
    /// tests to exercise the working-hours duration math across shift windows.
    /// </summary>
    public static async Task<Guid> SeedBlockRequestAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        Guid orderItemProcessId,
        Guid requestedByUserId,
        RequestStatus finalStatus = RequestStatus.Pending,
        DateTime? createdAt = null,
        DateTime? handledAt = null)
    {
        using var scope = factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var br = BlockRequest.CreateForProcess(
            tenantId, orderItemProcessId, requestedByUserId, requestNote: "test");

        switch (finalStatus)
        {
            case RequestStatus.Approved:
                br.Approve(requestedByUserId, "test-approved");
                break;
            case RequestStatus.Resolved:
                br.Approve(requestedByUserId, "test-approved");
                br.Resolve(requestedByUserId);
                break;
            case RequestStatus.Rejected:
                br.Reject(requestedByUserId, "test-rejected");
                break;
        }

        ordersDb.BlockRequests.Add(br);
        await ordersDb.SaveChangesAsync();

        if (createdAt.HasValue || handledAt.HasValue)
        {
            await ordersDb.BlockRequests
                .IgnoreQueryFilters()
                .Where(b => b.Id == br.Id)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(b => b.CreatedAt, createdAt ?? br.CreatedAt)
                    .SetProperty(b => b.HandledAt, handledAt ?? br.HandledAt));
        }

        return br.Id;
    }

    /// <summary>
    /// Seeds a SubProcess template + an OrderItemSubProcess on an existing
    /// OIP + an OrderItemSubProcessLog with override-able start/end times.
    /// Returns the log id. Used by /reports/work-efficiency tests to exercise
    /// the wall-clock-union math for "Aktivno na procesima."
    /// </summary>
    public static async Task<Guid> SeedSubProcessLogAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        Guid orderItemProcessId,
        Guid processId,
        Guid userId,
        DateTime startTime,
        DateTime endTime)
    {
        using var scope = factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var productionDb = scope.ServiceProvider.GetRequiredService<ProductionDbContext>();

        var subProcessName = $"SP-{Guid.NewGuid():N}".Substring(0, 10);
        // Process.AddSubProcess is the public factory path; SubProcess.Create is internal.
        var process = await productionDb.Processes
            .IgnoreQueryFilters()
            .SingleAsync(p => p.Id == processId);
        var subProcess = process.AddSubProcess(subProcessName, sequenceOrder: 1);
        await productionDb.SaveChangesAsync();

        var oip = await ordersDb.OrderItemProcesses
            .IgnoreQueryFilters()
            .SingleAsync(p => p.Id == orderItemProcessId);
        var oisp = oip.AddSubProcess(subProcess.Id);
        await ordersDb.SaveChangesAsync();

        var log = OrderItemSubProcessLog.Start(tenantId, oisp.Id, userId);
        log.End();
        ordersDb.OrderItemSubProcessLogs.Add(log);
        await ordersDb.SaveChangesAsync();

        // Override the auto-generated timestamps.
        var duration = (int)(endTime - startTime).TotalSeconds;
        await ordersDb.OrderItemSubProcessLogs
            .IgnoreQueryFilters()
            .Where(l => l.Id == log.Id)
            .ExecuteUpdateAsync(set => set
                .SetProperty(l => l.StartTime, startTime)
                .SetProperty(l => l.EndTime, endTime)
                .SetProperty(l => l.DurationMinutes, duration));

        return log.Id;
    }

    public static async Task<Guid> SeedNotificationAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        Guid userId,
        string? title = null)
    {
        using var scope = factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var notification = Notification.Create(
            tenantId,
            userId,
            NotificationType.DeadlineWarning,
            title ?? $"Notif-{Guid.NewGuid():N}".Substring(0, 12),
            message: "Cross-tenant test notification");

        ordersDb.Notifications.Add(notification);
        await ordersDb.SaveChangesAsync();
        return notification.Id;
    }

    private sealed record LoginResponse(string Token, string RefreshToken);
}

public sealed record SeededTenant(
    Guid TenantId,
    string TenantCode,
    Guid UserId,
    string Email,
    string Password,
    UserRole Role);
