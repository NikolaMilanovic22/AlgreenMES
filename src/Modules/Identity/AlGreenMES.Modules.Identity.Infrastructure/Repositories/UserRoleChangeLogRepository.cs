using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Identity.Infrastructure.Repositories;

public class UserRoleChangeLogRepository : IUserRoleChangeLogRepository
{
    private readonly IdentityDbContext _dbContext;

    public UserRoleChangeLogRepository(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(UserRoleChangeLog log, CancellationToken cancellationToken = default)
    {
        await _dbContext.UserRoleChangeLogs.AddAsync(log, cancellationToken);
    }

    public async Task<IReadOnlyList<UserRoleChangeWithActor>> GetForUserWithActorAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Outer query is tenant-scoped via HasQueryFilter on
        // UserRoleChangeLog.TenantId; the actor lookup bypasses the
        // tenant filter because a SuperAdmin sits in a different tenant
        // and we still want to render their name. The log row itself
        // can't leak — that's guarded by the outer filter.
        var rows = await _dbContext.UserRoleChangeLogs
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.ChangedAt)
            .Select(l => new
            {
                Log = l,
                ChangedByUserFullName = _dbContext.Users
                    .IgnoreQueryFilters()
                    .Where(u => u.Id == l.ChangedByUserId)
                    .Select(u => (string?)(u.FirstName + " " + u.LastName))
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new UserRoleChangeWithActor(r.Log, r.ChangedByUserFullName)).ToList();
    }
}
