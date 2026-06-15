using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Identity.Infrastructure.Repositories;

public class LoginAttemptRepository : ILoginAttemptRepository
{
    private readonly IdentityDbContext _dbContext;

    public LoginAttemptRepository(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(LoginAttempt attempt, CancellationToken cancellationToken = default)
    {
        await _dbContext.LoginAttempts.AddAsync(attempt, cancellationToken);
    }

    public async Task<IReadOnlyList<LoginAttempt>> GetRecentForEmailAsync(
        Guid tenantId, string email, int limit, CancellationToken cancellationToken = default)
    {
        // LoginAttempt is the one entity with nullable TenantId, so it
        // doesn't have a HasQueryFilter — we filter explicitly here so an
        // admin in tenant A can't see attempts logged against tenant B for
        // the same email (rare but possible cross-tenant probe).
        return await _dbContext.LoginAttempts
            .Where(la => la.TenantId == tenantId && la.Email == email)
            .OrderByDescending(la => la.AttemptedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
