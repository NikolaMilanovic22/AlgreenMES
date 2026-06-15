using System.Security.Claims;
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

        var scanCount = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var openSessionsCount = await ScanAsync(stoppingToken);
                scanCount++;
                // Heartbeat — one Information log every successful scan so a
                // silent ExecuteAsync crash (e.g. ValueTuple cast bug from
                // 03.06.2026 or tenant-context bug) shows up in Sentry as
                // "no heartbeat" rather than waiting for users to notice
                // that auto-logout stopped working. Cheap: 30 lines/hour.
                _logger.LogInformation(
                    "AutoLogoutBackgroundService scan #{ScanCount} OK, {OpenSessions} open session(s) processed",
                    scanCount, openSessionsCount);
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

    /// <returns>Number of open sessions enumerated in this scan.</returns>
    private async Task<int> ScanAsync(CancellationToken ct)
    {
        // Bootstrap scope: just enumerates open sessions across all tenants.
        // Uses IgnoreQueryFilters() so the OrdersDbContext tenant filter does
        // not blank the result (we have no HTTP context here, so the filter
        // would evaluate to TenantId == Guid.Empty and match nothing).
        // NOTE: project to an anonymous type — projecting straight to a
        // ValueTuple via .Select(ValueTuple.Create(...)) makes Npgsql try
        // to read a Postgres `record` and throws InvalidCastException at
        // scan time, silently breaking the service. Materialize first,
        // tuple-ify in-memory.
        List<(Guid TenantId, Guid UserId)> openSessions;
        using (var bootstrap = _scopeFactory.CreateScope())
        {
            var ordersDb = bootstrap.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var rows = await ordersDb.WorkSessions
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(ws => ws.CheckOutTime == null)
                .Select(ws => new { ws.TenantId, ws.UserId })
                .Distinct()
                .ToListAsync(ct);
            openSessions = rows.Select(x => (x.TenantId, x.UserId)).ToList();
        }

        if (openSessions.Count == 0) return 0;

        foreach (var s in openSessions)
        {
            // Per-session scope with a synthetic HttpContext carrying the
            // session's tenant + user claims. Downstream mediator handlers
            // (e.g. AutoCheckOutCommandHandler, repositories) rely on the
            // tenant query filter resolved via ICurrentUserService — without
            // this they'd see "no active session" and silently no-op.
            using var perUserScope = _scopeFactory.CreateScope();
            var httpAccessor = perUserScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpAccessor.HttpContext = BuildBackgroundHttpContext(s.TenantId, s.UserId);
            var mediator = perUserScope.ServiceProvider.GetRequiredService<IMediator>();

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

        return openSessions.Count;
    }

    private static DefaultHttpContext BuildBackgroundHttpContext(Guid tenantId, Guid userId)
    {
        // SuperAdmin role bypasses any [Authorize(Roles=...)] checks in handlers
        // we route through; tenant_id + sub claims feed CurrentUserService so
        // OrdersDbContext's tenant filter resolves correctly for this scan.
        var claims = new[]
        {
            new Claim("tenant_id", tenantId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, "SuperAdmin"),
        };
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "AutoLogoutBackgroundService")),
        };
    }
}
