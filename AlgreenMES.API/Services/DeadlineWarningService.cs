using AlGreenMES.Modules.Orders.Application.DTOs.Events;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using AlGreenMES.Modules.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlgreenMES.API.Services;

public class DeadlineWarningService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeadlineWarningService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(30);

    public DeadlineWarningService(IServiceScopeFactory scopeFactory, ILogger<DeadlineWarningService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DeadlineWarningService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckDeadlinesAsync(stoppingToken);
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — exit the loop silently
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking deadlines.");
                await Task.Delay(TimeSpan.FromMinutes(1), CancellationToken.None);
            }
        }
    }

    private async Task CheckDeadlinesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var tenancyDb = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var eventService = scope.ServiceProvider.GetRequiredService<IProductionEventService>();

        // Background service runs without an HTTP context — no JWT, so HasQueryFilter would throw.
        // Iterate tenants explicitly with IgnoreQueryFilters; every Where clause carries the tenant id.
        var tenants = await tenancyDb.Tenants
            .AsNoTracking()
            .Where(t => t.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var tenant in tenants)
        {
            var settings = await tenancyDb.TenantSettings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenant.Id, cancellationToken);

            var defaultWarningDays = settings?.DefaultWarningDays ?? 7;
            var defaultCriticalDays = settings?.DefaultCriticalDays ?? 3;

            var activeOrders = await ordersDb.Orders
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(o => o.Items)
                    .ThenInclude(i => i.Processes)
                .Where(o => o.TenantId == tenant.Id && o.Status == OrderStatus.Active)
                .ToListAsync(cancellationToken);

            var today = DateTime.UtcNow.Date;

            foreach (var order in activeOrders)
            {
                var warningDays = order.CustomWarningDays ?? defaultWarningDays;
                var criticalDays = order.CustomCriticalDays ?? defaultCriticalDays;
                var daysRemaining = (order.DeliveryDate.Date - today).Days;

                if (daysRemaining > warningDays) continue;

                var level = daysRemaining <= criticalDays ? "Critical" : "Warning";
                var notificationType = daysRemaining <= criticalDays
                    ? NotificationType.DeadlineCritical
                    : NotificationType.DeadlineWarning;

                // Check if we already sent this level of notification today
                var alreadyNotified = await ordersDb.Notifications
                    .IgnoreQueryFilters()
                    .AnyAsync(n => n.TenantId == tenant.Id
                        && n.ReferenceType == "Order"
                        && n.ReferenceId == order.Id
                        && n.Type == notificationType
                        && n.CreatedAt.Date == today,
                        cancellationToken);

                if (alreadyNotified) continue;

                // Collect distinct processIds with pending/in-progress work
                var processIds = order.Items
                    .SelectMany(i => i.Processes)
                    .Where(p => !p.IsWithdrawn && p.Status != ProcessStatus.Completed)
                    .Select(p => p.ProcessId)
                    .Distinct()
                    .ToList();

                // NotifyDeadlineWarningAsync handles per-user notifications + push
                await eventService.NotifyDeadlineWarningAsync(new DeadlineWarningEvent(
                    order.Id,
                    order.OrderNumber,
                    order.DeliveryDate,
                    daysRemaining,
                    level,
                    tenant.Id,
                    processIds), cancellationToken);

                _logger.LogInformation(
                    "{Level} deadline warning for order {OrderNumber}: {DaysRemaining} days remaining.",
                    level, order.OrderNumber, daysRemaining);
            }
        }
    }
}
