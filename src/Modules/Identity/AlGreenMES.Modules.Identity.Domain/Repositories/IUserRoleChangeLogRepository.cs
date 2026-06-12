using AlGreenMES.Modules.Identity.Domain.Entities;

namespace AlGreenMES.Modules.Identity.Domain.Repositories;

public interface IUserRoleChangeLogRepository
{
    Task AddAsync(UserRoleChangeLog log, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns role-change rows for the given user, newest first, paired
    /// with the actor's full name (LEFT JOIN — actor may have been
    /// deactivated since, but the join still resolves).
    /// </summary>
    Task<IReadOnlyList<UserRoleChangeWithActor>> GetForUserWithActorAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository-shaped projection of a role-change log entry plus the
/// resolved actor name. Lives in Domain so the Application layer can
/// consume it without touching EF types.
/// </summary>
public record UserRoleChangeWithActor(
    UserRoleChangeLog Log,
    string? ChangedByUserFullName);
