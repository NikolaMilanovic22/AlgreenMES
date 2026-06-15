using AlGreenMES.Modules.Identity.Domain.Entities;

namespace AlGreenMES.Modules.Identity.Domain.Repositories;

public interface ILoginAttemptRepository
{
    Task AddAsync(LoginAttempt attempt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent login attempts for an email within a single
    /// tenant. Newest first, capped at <paramref name="limit"/> rows. Used
    /// by the admin user-detail UI to surface the audit trail per user.
    /// </summary>
    Task<IReadOnlyList<LoginAttempt>> GetRecentForEmailAsync(
        Guid tenantId,
        string email,
        int limit,
        CancellationToken cancellationToken = default);
}
