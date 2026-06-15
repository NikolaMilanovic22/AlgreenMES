namespace AlGreenMES.Modules.Orders.Application.Interfaces;

/// <summary>
/// Cross-module read of the user's qualified process IDs (Identity.UserProcesses).
/// Orders.Application can't reference Identity directly; Orders.Infrastructure
/// implements this via the same IdentityDbContext injection pattern already
/// used by ReportingQueryService.
///
/// Needed by AutoCheckOutCommandHandler (Bojan 04.06.2026 — process-level
/// processes like Krojenje must also be paused on auto-logout, mirroring the
/// tablet's manual-logout PauseStation-per-user.process orchestration).
/// </summary>
public interface IUserProcessLookup
{
    Task<IReadOnlyList<System.Guid>> GetUserProcessIdsAsync(System.Guid userId, CancellationToken cancellationToken = default);
}
