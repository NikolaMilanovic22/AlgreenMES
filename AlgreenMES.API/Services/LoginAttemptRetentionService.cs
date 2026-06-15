using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlgreenMES.API.Services;

/// <summary>
/// Nightly retention sweep for the login_attempts audit table. Audit data
/// older than <see cref="RetentionDays"/> is rarely actionable — incident
/// response that needs it lives in the SIEM (Sentry / Serilog file logs),
/// not the application DB. Trimming keeps the table small enough that the
/// per-user history query stays sub-millisecond even after years of use.
///
/// First run fires shortly after startup (so a freshly-deployed instance
/// catches up if the previous instance was offline at midnight). Subsequent
/// runs fire every 24 hours.
///
/// The user_role_change_logs table is intentionally NOT trimmed here —
/// role transitions are rare events (most users 0–3 in a lifetime), the
/// table won't grow meaningfully, and forensic queries usually want the
/// full history regardless of age.
/// </summary>
public class LoginAttemptRetentionService : BackgroundService
{
    private const int RetentionDays = 90;
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LoginAttemptRetentionService> _logger;

    public LoginAttemptRetentionService(
        IServiceScopeFactory scopeFactory,
        ILogger<LoginAttemptRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger the first run so app startup isn't competing with the
        // retention DELETE for connection-pool slots / locks.
        try
        {
            await Task.Delay(FirstRunDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await TryTrimAsync(stoppingToken);

            try
            {
                await Task.Delay(RunInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task TryTrimAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var cutoffUtc = DateTime.UtcNow - TimeSpan.FromDays(RetentionDays);

            // ExecuteDeleteAsync compiles to a single SQL DELETE — doesn't
            // materialise entities. Critical: on a table with millions of
            // rows the materialising path would OOM.
            var deleted = await db.LoginAttempts
                .Where(la => la.AttemptedAt < cutoffUtc)
                .ExecuteDeleteAsync(stoppingToken);

            if (deleted > 0)
            {
                _logger.LogInformation(
                    "LoginAttemptRetention: trimmed {Deleted} login_attempts rows older than {Cutoff:O}",
                    deleted, cutoffUtc);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Don't let a single bad run kill the service — log and retry
            // on the next interval. A truly persistent failure shows up as
            // recurring error events in Sentry.
            _logger.LogError(ex, "LoginAttemptRetention sweep failed; will retry at next interval.");
        }
    }
}
