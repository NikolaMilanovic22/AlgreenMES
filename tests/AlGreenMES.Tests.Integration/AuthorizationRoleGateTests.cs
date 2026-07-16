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
/// Endpoint-level role gate coverage. WorkflowTests / OrdersTests run
/// every workflow happy path with an Admin user (which satisfies every
/// role group); they prove the endpoint works, NOT that it's properly
/// locked down.
///
/// A typo in `RoleGroups.CoordinatorUp`, a missing `[Authorize(Roles=...)]`
/// on a new endpoint, or a refactor that re-names a role string would
/// silently let a Department (worker) role mutate things only coordinators
/// should touch. Discovered the painful way when a worker accidentally
/// withdraws a colleague's process from the tablet because the gate was
/// missing.
///
/// These tests seed a non-privileged role and assert 403 on the
/// privileged endpoints.
/// </summary>
public class AuthorizationRoleGateTests : IntegrationTestBase
{
    public AuthorizationRoleGateTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    // ──────────────────────────────────────────────────────────────────────
    // User-management endpoints — role gate matrix (UsersController)
    // POST/PUT/DELETE users + reset-password require AdminUp; GET users
    // requires ManagerUp. A dropped/mistyped [Authorize(Roles=...)] would
    // silently let a Manager mutate users or a worker enumerate them.
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateUser_AsManager_Returns403()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, role: UserRole.Manager);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync("/api/users", new
        {
            Email = $"new-{Guid.NewGuid():N}@test.local",
            Password = "NewPass123!",
            FirstName = "New",
            LastName = "User",
            Role = "Department",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUsers_AsDepartment_Returns403()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, role: UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.GetAsync("/api/users");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ResetPassword_AsManager_Returns403()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, role: UserRole.Manager);
        var targetId = await TestDataSeeder.SeedAdditionalUserAsync(Factory, t.TenantId, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync($"/api/users/{targetId}/reset-password", new
        {
            NewPassword = "NewPass123!",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateUser_NonSuperAdminOverridingTenant_IsForbidden()
    {
        // Only a SuperAdmin may pass request.TenantId to create a user in
        // another tenant; a tenant Admin doing so must be rejected (otherwise
        // an Admin could inject a user row into a foreign tenant). `a` is Admin,
        // so it clears the AdminUp gate and reaches the handler's override guard.
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory, roleForA: UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, a);

        var resp = await client.PostAsJsonAsync("/api/users", new
        {
            Email = $"inject-{Guid.NewGuid():N}@test.local",
            Password = "NewPass123!",
            FirstName = "Injected",
            LastName = "User",
            Role = "Department",
            TenantId = b.TenantId, // foreign tenant
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var err = await resp.Content.ReadFromJsonAsync<RoleGateErrorBody>();
        err!.Error.Code.Should().Be("FORBIDDEN_TENANT_OVERRIDE");
    }

    private sealed record RoleGateErrorBody(RoleGateErrorPayload Error);
    private sealed record RoleGateErrorPayload(string Code, string Message);

    [Fact]
    public async Task UnblockProcess_AsDepartmentRole_Returns403()
    {
        // CoordinatorUp only — a tablet worker must not be able to clear
        // a blockade their coordinator placed.
        var (t, oipId) = await SeedBlockedProcessAsWorkerAsync();
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/unblock",
            new { userId = t.UserId, resetTime = false });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RestartProcess_AsDepartmentRole_Returns403()
    {
        // CoordinatorUp only — restart can wipe work history and reset
        // the timer (resetTime=true), so workers must not have it.
        var (t, oipId) = await SeedCompletedProcessAsWorkerAsync();
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/restart",
            new { resetTime = true });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WithdrawProcess_AsDepartmentRole_Returns403()
    {
        // CoordinatorUp only — withdraw pulls work off the floor and
        // adds an audit row; workers can't do this to a peer's work.
        var (t, oipId) = await SeedInProgressProcessAsWorkerAsync();
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/order-item-processes/{oipId}/withdraw",
            new { userId = t.UserId, reason = "peer's work" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ActivateOrder_AsDepartmentRole_Returns403()
    {
        // CoordinatorUp only — activating an order pushes it to the
        // floor; only coordinators decide what's worked on.
        var (t, orderId) = await SeedDraftOrderAsWorkerAsync();
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsync($"/api/orders/{orderId}/activate", content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ApproveBlockRequest_AsDepartmentRole_Returns403()
    {
        // CoordinatorUp only — the whole point of the request/approve
        // flow is to put coordinator approval between worker request
        // and floor impact. A worker self-approving defeats the gate.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        // Seed a pending block request (use a separate Admin tenant
        // session would be cleaner, but we can also reach into the DB
        // directly since the test only cares about the HTTP-layer gate).
        // Simplest: use any random Guid — the gate must fire BEFORE the
        // handler even loads the entity.
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync(
            $"/api/block-requests/{Guid.NewGuid()}/approve",
            new { handledByUserId = t.UserId, note = "trying to self-approve" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the role check must fire BEFORE the handler — if the gate is missing, we'd see 404 (entity not found) instead");
    }

    [Fact]
    public async Task CreateOrder_AsDepartmentRole_Returns403()
    {
        // ManagerOrSales only — workers don't create orders.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        using var form = new MultipartFormDataContent
        {
            { new StringContent("WORKER-1"), "OrderNumber" },
            { new StringContent(DateTime.UtcNow.AddDays(7).ToString("O")), "DeliveryDate" },
            { new StringContent("3"), "Priority" },
            { new StringContent("Standard"), "OrderType" },
        };

        var resp = await client.PostAsync("/api/orders", form);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------------
    // Setup: seed a tenant with a Department-role user, plus the matching
    // OIP state. The Department user is what we authenticate as.
    // ---------------------------------------------------------------------

    private async Task<(SeededTenant Tenant, Guid OipId)> SeedBlockedProcessAsWorkerAsync()
        => await SeedProcessAsWorkerAsync(ProcessStatus.Blocked);

    private async Task<(SeededTenant Tenant, Guid OipId)> SeedInProgressProcessAsWorkerAsync()
        => await SeedProcessAsWorkerAsync(ProcessStatus.InProgress);

    private async Task<(SeededTenant Tenant, Guid OipId)> SeedCompletedProcessAsWorkerAsync()
        => await SeedProcessAsWorkerAsync(ProcessStatus.Completed);

    private async Task<(SeededTenant Tenant, Guid OipId)> SeedProcessAsWorkerAsync(ProcessStatus status)
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId);
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId);
        var oipId = await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId, status);
        using (var scope = Factory.Services.CreateScope())
        {
            var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var orderId = await ordersDb.OrderItemProcesses
                .IgnoreQueryFilters()
                .Where(p => p.Id == oipId)
                .Select(p => p.OrderItem.OrderId)
                .SingleAsync();
            await TestDataSeeder.SetOrderStatusAsync(Factory, orderId, OrderStatus.Active);
        }
        return (t, oipId);
    }

    private async Task<(SeededTenant Tenant, Guid OrderId)> SeedDraftOrderAsWorkerAsync()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var orderId = await TestDataSeeder.SeedOrderAsync(Factory, t.TenantId, t.UserId);
        return (t, orderId);
    }
}
