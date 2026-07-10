using AlgreenMES.API.BackgroundServices;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using AlGreenMES.Modules.Tenancy.Domain.Entities;
using AlGreenMES.Modules.Tenancy.Infrastructure.Persistence;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// Integration coverage for the daily subscription reminder
/// (Saša 18.06.2026 bonus). The service is registered as a Singleton in
/// Program.cs so the same instance backs both the background loop and
/// the SA manual-trigger endpoint; we resolve it from the factory and
/// call <see cref="BillingReminderService.RunOnceAsync"/> directly to
/// skip the 24h cadence.
///
/// Each test seeds its own tenant + payment fixture by writing the DB
/// directly so it can pin periodStart / periodEnd around "today" without
/// fighting the future-paidAt guard on the public endpoint.
/// </summary>
public class BillingReminderServiceTests : IntegrationTestBase
{
    public BillingReminderServiceTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task RunOnce_TenantInExpiringWindow_CreatesWarningForAdminOnly()
    {
        var (tenant, admin, otherRoleUserIds) = await SeedTenantWithAdminAndOtherRolesAsync();
        await SeedPaymentAsync(tenant.Id, daysFromNowToPeriodEnd: 7);

        var service = Factory.Services.GetRequiredService<BillingReminderService>();
        await service.RunOnceAsync(CancellationToken.None);

        var notifications = await NotificationsForUserAsync(admin.Id);
        notifications.Should().ContainSingle();
        notifications[0].Type.Should().Be(NotificationType.SubscriptionExpiring);
        notifications[0].ReferenceType.Should().Be("Subscription");
        notifications[0].ReferenceId.Should().Be(tenant.Id);

        foreach (var userId in otherRoleUserIds)
        {
            (await NotificationsForUserAsync(userId)).Should().BeEmpty(
                "only Admin users get billing nudges");
        }
    }

    [Fact]
    public async Task RunOnce_TenantExpired_CreatesExpiredTypeNotWarning()
    {
        var (tenant, admin, _) = await SeedTenantWithAdminAndOtherRolesAsync();
        await SeedPaymentAsync(tenant.Id, daysFromNowToPeriodEnd: -3);

        var service = Factory.Services.GetRequiredService<BillingReminderService>();
        await service.RunOnceAsync(CancellationToken.None);

        var notifications = await NotificationsForUserAsync(admin.Id);
        notifications.Should().ContainSingle();
        notifications[0].Type.Should().Be(NotificationType.SubscriptionExpired);
    }

    [Fact]
    public async Task RunOnce_TenantWellBeyondThreshold_DoesNotNotify()
    {
        var (tenant, admin, _) = await SeedTenantWithAdminAndOtherRolesAsync();
        // 30 days out — comfortably past the 14-day threshold; nothing
        // urgent to nudge yet.
        await SeedPaymentAsync(tenant.Id, daysFromNowToPeriodEnd: 30);

        var service = Factory.Services.GetRequiredService<BillingReminderService>();
        await service.RunOnceAsync(CancellationToken.None);

        (await NotificationsForUserAsync(admin.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task RunOnce_TenantWithNoPayments_DoesNotNotify()
    {
        var (_, admin, _) = await SeedTenantWithAdminAndOtherRolesAsync();
        // No payment seeded — never-paid tenants don't get harassed at
        // user level. SA sees them via the "Uplata kasni" status column.

        var service = Factory.Services.GetRequiredService<BillingReminderService>();
        await service.RunOnceAsync(CancellationToken.None);

        (await NotificationsForUserAsync(admin.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task RunOnce_BlockedTenant_DoesNotNotify()
    {
        var (tenant, admin, _) = await SeedTenantWithAdminAndOtherRolesAsync();
        await SeedPaymentAsync(tenant.Id, daysFromNowToPeriodEnd: 5);
        await BlockTenantAsync(tenant.Id);

        var service = Factory.Services.GetRequiredService<BillingReminderService>();
        await service.RunOnceAsync(CancellationToken.None);

        // Blocked tenant Admins can't log in anyway; nudging them is
        // noise and the SA already gets visual signal via the red Tag
        // on the Firme list.
        (await NotificationsForUserAsync(admin.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task RunOnce_CalledTwiceSameDay_IsIdempotent()
    {
        var (tenant, admin, _) = await SeedTenantWithAdminAndOtherRolesAsync();
        await SeedPaymentAsync(tenant.Id, daysFromNowToPeriodEnd: 7);

        var service = Factory.Services.GetRequiredService<BillingReminderService>();
        await service.RunOnceAsync(CancellationToken.None);
        await service.RunOnceAsync(CancellationToken.None);

        // Second invocation must not double the bell — the SA-only manual
        // trigger endpoint can be hammered without flooding recipients.
        (await NotificationsForUserAsync(admin.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task RunOnce_TenantExactlyAtFourteenDayBoundary_Notifies()
    {
        // ExpiringThresholdDays = 14; the guard is `paidThrough > thresholdEnd`,
        // so paidThrough == today+14 is INSIDE the window → notify.
        var (tenant, admin, _) = await SeedTenantWithAdminAndOtherRolesAsync();
        await SeedPaymentAsync(tenant.Id, daysFromNowToPeriodEnd: 14);

        var service = Factory.Services.GetRequiredService<BillingReminderService>();
        await service.RunOnceAsync(CancellationToken.None);

        var notifications = await NotificationsForUserAsync(admin.Id);
        notifications.Should().ContainSingle();
        notifications[0].Type.Should().Be(NotificationType.SubscriptionExpiring);
    }

    [Fact]
    public async Task RunOnce_TenantOneDayPastBoundary_DoesNotNotify()
    {
        // today+15 is one day beyond the 14-day window → silent.
        var (tenant, admin, _) = await SeedTenantWithAdminAndOtherRolesAsync();
        await SeedPaymentAsync(tenant.Id, daysFromNowToPeriodEnd: 15);

        var service = Factory.Services.GetRequiredService<BillingReminderService>();
        await service.RunOnceAsync(CancellationToken.None);

        (await NotificationsForUserAsync(admin.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task RunOnce_TenantWithTwoAdmins_NotifiesBoth_AndIsIdempotent()
    {
        var (tenant, admin1, admin2) = await SeedTenantWithTwoAdminsAsync();
        await SeedPaymentAsync(tenant.Id, daysFromNowToPeriodEnd: 7);

        var service = Factory.Services.GetRequiredService<BillingReminderService>();
        await service.RunOnceAsync(CancellationToken.None);
        // Re-run same day must not double up (per-user/day idempotency).
        await service.RunOnceAsync(CancellationToken.None);

        var n1 = await NotificationsForUserAsync(admin1.Id);
        var n2 = await NotificationsForUserAsync(admin2.Id);
        n1.Should().ContainSingle();
        n1[0].Type.Should().Be(NotificationType.SubscriptionExpiring);
        n2.Should().ContainSingle();
        n2[0].Type.Should().Be(NotificationType.SubscriptionExpiring);
    }

    // ──────────────────────────────────────────────────────────────────
    // Fixture helpers — write the DB directly so we can pin periodStart /
    // periodEnd around "today" without arguing with the future-paidAt
    // guard on the public POST /tenants/{id}/payments endpoint.
    // ──────────────────────────────────────────────────────────────────

    private async Task<(Tenant tenant, User admin1, User admin2)> SeedTenantWithTwoAdminsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var tenancyDb = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var identityDb = scope.ServiceProvider.GetRequiredService<AlGreenMES.Modules.Identity.Infrastructure.Persistence.IdentityDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<AlGreenMES.Modules.Identity.Application.Services.IPasswordHasher>();

        var code = "BR2" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var tenant = Tenant.Create($"Tenant {code}", code);
        tenancyDb.Tenants.Add(tenant);
        await tenancyDb.SaveChangesAsync();

        var hash = passwordHasher.HashPassword("Pass123!");
        var admin1 = User.Create(tenant.Id, $"admin1-{Guid.NewGuid():N}@test.local", hash, "Test", "AdminOne", UserRole.Admin);
        var admin2 = User.Create(tenant.Id, $"admin2-{Guid.NewGuid():N}@test.local", hash, "Test", "AdminTwo", UserRole.Admin);
        identityDb.Users.AddRange(admin1, admin2);
        await identityDb.SaveChangesAsync();

        return (tenant, admin1, admin2);
    }

    private async Task<(Tenant tenant, User admin, IReadOnlyList<Guid> nonAdminUserIds)> SeedTenantWithAdminAndOtherRolesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var tenancyDb = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var identityDb = scope.ServiceProvider.GetRequiredService<AlGreenMES.Modules.Identity.Infrastructure.Persistence.IdentityDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<AlGreenMES.Modules.Identity.Application.Services.IPasswordHasher>();

        var code = "BRT" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var tenant = Tenant.Create($"Tenant {code}", code);
        tenancyDb.Tenants.Add(tenant);
        await tenancyDb.SaveChangesAsync();

        var hash = passwordHasher.HashPassword("Pass123!");
        var admin = User.Create(tenant.Id, $"admin-{Guid.NewGuid():N}@test.local", hash, "Test", "Admin", UserRole.Admin);
        var manager = User.Create(tenant.Id, $"mgr-{Guid.NewGuid():N}@test.local", hash, "Test", "Manager", UserRole.Manager);
        var coordinator = User.Create(tenant.Id, $"coord-{Guid.NewGuid():N}@test.local", hash, "Test", "Coordinator", UserRole.Coordinator);
        identityDb.Users.AddRange(admin, manager, coordinator);
        await identityDb.SaveChangesAsync();

        return (tenant, admin, new[] { manager.Id, coordinator.Id });
    }

    private async Task SeedPaymentAsync(Guid tenantId, int daysFromNowToPeriodEnd)
    {
        using var scope = Factory.Services.CreateScope();
        var tenancyDb = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();

        var today = DateTime.UtcNow.Date;
        var periodEnd = today.AddDays(daysFromNowToPeriodEnd);
        // Anchor periodStart safely in the past so the paidThrough query
        // (which filters periodStart <= today) always counts it. paidAt
        // matches periodStart so the row passes the future-paidAt guard
        // when read back through the SA UI later.
        var periodStart = today.AddDays(-30);

        var payment = TenantPayment.Create(
            tenantId,
            periodStart,
            periodEnd,
            amount: 100m,
            currency: "EUR",
            paidAt: periodStart,
            invoiceNumber: null,
            notes: null);
        tenancyDb.TenantPayments.Add(payment);
        await tenancyDb.SaveChangesAsync();
    }

    private async Task BlockTenantAsync(Guid tenantId)
    {
        using var scope = Factory.Services.CreateScope();
        var tenancyDb = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var tenant = await tenancyDb.Tenants.FirstAsync(t => t.Id == tenantId);
        tenant.Block("test block");
        await tenancyDb.SaveChangesAsync();
    }

    private async Task<List<Notification>> NotificationsForUserAsync(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        // IgnoreQueryFilters because the test runs outside a JWT-bound
        // tenant scope and Notification is a TenantEntity — the default
        // filter drops every row when there's no current tenant.
        return await ordersDb.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.UserId == userId)
            .ToListAsync();
    }
}
