using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using AlGreenMES.Modules.Identity.Infrastructure.Persistence;

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
}
