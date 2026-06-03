using AlGreenMES.Modules.Orders.Application.Queries.GetActiveWorkSession;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AlgreenMES.API.Services;

// Proactive safety net for auto-logout (Bojan 03.06.2026 — testing showed
// the tablet-driven + lazy on-poll closures missed sessions when the tablet
// tab went idle, since react-query pauses refetchInterval for background
// tabs). This service scans every couple of minutes for open work sessions
// across all tenants and asks GetActiveWorkSessionQuery for each — the
// query handler's existing lazy safety net then fires AutoCheckOutCommand
// (with logoutAt-backdated checkout) for any session past its cap. We
// deliberately route through the mediator so cap math + notifications +
// WasAutoClosed flag stay in one place.
public class AutoLogoutBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoLogoutBackgroundService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(2);

    public AutoLogoutBackgroundService(IServiceScopeFactory scopeFactory, ILogger<AutoLogoutBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutoLogoutBackgroundService started, interval {Interval}", _checkInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAsync(stoppingToken);
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // graceful shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoLogoutBackgroundService scan failed");
                await Task.Delay(TimeSpan.FromMinutes(1), CancellationToken.None);
            }
        }
    }

    private async Task ScanAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var openSessions = await ordersDb.WorkSessions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(ws => ws.CheckOutTime == null)
            .Select(ws => new { ws.TenantId, ws.UserId })
            .Distinct()
            .ToListAsync(ct);

        if (openSessions.Count == 0) return;

        foreach (var s in openSessions)
        {
            try
            {
                // The query handler internally fires AutoCheckOutCommand when
                // logoutAt is in the past. We don't care about the return value.
                await mediator.Send(new GetActiveWorkSessionQuery(s.TenantId, s.UserId), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Auto-close scan failed for tenant {TenantId} user {UserId}",
                    s.TenantId, s.UserId);
            }
        }
    }
}
