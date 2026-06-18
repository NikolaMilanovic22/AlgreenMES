using System.Text.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using AlGreenMES.Modules.Tenancy.Domain.Repositories;
using AlGreenMES.Modules.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlgreenMES.API.BackgroundServices;

/// <summary>
/// Once a day, scans all tenants whose subscription is within 14 days of
/// expiry (or already expired) and posts a SubscriptionExpiring
/// notification to every Admin user of that tenant. Blocked / unpaid
/// tenants don't get spammed at the user level — only Admins of
/// tenants whose subscription is approaching or past its end.
///
/// Idempotency: at most one SubscriptionExpiring notification per
/// (user, day) so re-running the job within the same UTC day is a
/// no-op. This means an SA can hit the manual trigger endpoint for
/// testing without flooding the bell.
///
/// Scheduling: a simple timer fires every hour and checks whether the
/// configured run hour (default 06:00 UTC ≈ 08:00 local) was already
/// processed today. Kept in-process to avoid pulling in Hangfire/Quartz
/// for one daily job.
/// </summary>
public class BillingReminderService : BackgroundService
{
    private const int RunHourUtc = 6;
    private const int ExpiringThresholdDays = 14;

    private readonly IServiceProvider _services;
    private readonly ILogger<BillingReminderService> _logger;
    private DateTime? _lastRunDay;

    public BillingReminderService(IServiceProvider services, ILogger<BillingReminderService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                var today = nowUtc.Date;
                if (nowUtc.Hour >= RunHourUtc && _lastRunDay != today)
                {
                    await RunOnceAsync(stoppingToken);
                    _lastRunDay = today;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BillingReminderService tick failed");
            }

            // Hourly tick — cheap. The actual work only runs once per day
            // thanks to the _lastRunDay guard.
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    /// <summary>
    /// Public entry point so the SA-only manual trigger endpoint can
    /// force a run on demand for testing.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var tenancyDb = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var paymentRepo = scope.ServiceProvider.GetRequiredService<ITenantPaymentRepository>();

        var today = DateTime.UtcNow.Date;
        var thresholdEnd = today.AddDays(ExpiringThresholdDays);

        // Active tenants only — blocked / inactive tenants don't get user-level
        // nudges; their Admins already can't log in.
        var tenants = await tenancyDb.Tenants
            .Where(t => t.IsActive)
            .Select(t => new { t.Id, t.Code })
            .ToListAsync(cancellationToken);

        var paidThroughByTenant = await paymentRepo.GetPaidThroughByTenantAsync(cancellationToken);

        var created = 0;
        var skippedAlreadyExists = 0;

        foreach (var tenant in tenants)
        {
            // Only nudge tenants whose paid period is within the threshold
            // window OR already past. Tenants paid up beyond 14 days OR
            // never-paid (no row) are silent.
            if (!paidThroughByTenant.TryGetValue(tenant.Id, out var paidThrough)) continue;
            if (paidThrough > thresholdEnd) continue;

            var daysRemaining = (int)Math.Round((paidThrough - today).TotalDays);
            // Two separate notification types so the FE bell can show
            // expired in red (error) and expiring in orange (warning).
            var notificationType = daysRemaining < 0
                ? NotificationType.SubscriptionExpired
                : NotificationType.SubscriptionExpiring;

            // Admin users of this tenant.
            var adminUserIds = await identityDb.Users
                .Where(u => u.TenantId == tenant.Id && u.IsActive && u.Role == UserRole.Admin)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            if (adminUserIds.Count == 0) continue;

            // Idempotency: skip recipients who already received a
            // SubscriptionExpiring notification today.
            var tomorrow = today.AddDays(1);
            var alreadyNotified = await ordersDb.Notifications
                .Where(n => adminUserIds.Contains(n.UserId)
                            && (n.Type == NotificationType.SubscriptionExpiring
                                || n.Type == NotificationType.SubscriptionExpired)
                            && n.CreatedAt >= today
                            && n.CreatedAt < tomorrow)
                .Select(n => n.UserId)
                .ToListAsync(cancellationToken);

            var recipients = adminUserIds.Except(alreadyNotified).ToList();
            if (recipients.Count == 0) { skippedAlreadyExists += adminUserIds.Count; continue; }

            var paramsJson = JsonSerializer.Serialize(new
            {
                daysRemaining,
                paidThrough = paidThrough.ToString("dd.MM.yyyy."),
            });

            // Title + message are filled in for legacy clients that don't
            // resolve the i18n template. FE rendering with paramsJson
            // takes precedence.
            var (title, message) = notificationType == NotificationType.SubscriptionExpired
                ? ("Pretplata je istekla", $"Pretplata je istekla {paidThrough:dd.MM.yyyy}.")
                : ("Pretplata uskoro ističe", $"Ostalo je još {daysRemaining} dana — pretplata ističe {paidThrough:dd.MM.yyyy}.");

            foreach (var userId in recipients)
            {
                var n = Notification.Create(
                    tenant.Id,
                    userId,
                    notificationType,
                    title,
                    message,
                    referenceType: "Subscription",
                    referenceId: tenant.Id,
                    paramsJson: paramsJson);
                await ordersDb.Notifications.AddAsync(n, cancellationToken);
                created++;
            }
        }

        await ordersDb.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("BillingReminderService run complete: {Created} created, {Skipped} skipped (already notified today)", created, skippedAlreadyExists);
    }
}
