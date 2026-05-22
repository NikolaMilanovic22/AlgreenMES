using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Identity.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IdentityDbContext _dbContext;

    public UserRepository(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users
            .Include(u => u.UserProcesses)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<User?> GetByIdWithProcessesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users
            .Include(u => u.UserProcesses)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<User?> GetByIdIgnoreFiltersAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Refresh flow runs unauthenticated — bypass HasQueryFilter, caller validates tenant explicitly.
        return await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Login runs unauthenticated (no JWT yet) — bypass HasQueryFilter and rely on the explicit tenantId.
        var normalizedEmail = email.Trim().ToLowerInvariant();
        return await _dbContext.Users
            .IgnoreQueryFilters()
            .Include(u => u.UserProcesses)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.TenantId == tenantId, cancellationToken);
    }

    public async Task<User?> GetByEmailWithProcessesAsync(string email, Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Login runs unauthenticated (no JWT yet) — bypass HasQueryFilter and rely on the explicit tenantId.
        var normalizedEmail = email.Trim().ToLowerInvariant();
        return await _dbContext.Users
            .IgnoreQueryFilters()
            .Include(u => u.UserProcesses)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.TenantId == tenantId, cancellationToken);
    }

    public async Task<IReadOnlyList<User>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        await _dbContext.Users.AddAsync(user, cancellationToken);
    }

    public async Task<bool> ExistsByEmailAsync(string email, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        return await _dbContext.Users
            .AnyAsync(u => u.Email == normalizedEmail && u.TenantId == tenantId, cancellationToken);
    }

    public async Task<IReadOnlyList<User>> GetByProcessIdAsync(Guid processId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users
            .Include(u => u.UserProcesses)
            .Where(u => u.TenantId == tenantId && u.IsActive && u.UserProcesses.Any(up => up.ProcessId == processId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<User>> GetDepartmentUsersWithProcessesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users
            .Include(u => u.UserProcesses)
            .Where(u => u.TenantId == tenantId && u.Role == UserRole.Department && u.IsActive)
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .ToListAsync(cancellationToken);
    }

    public void Delete(User user)
    {
        _dbContext.Users.Remove(user);
    }

    public async Task<int> CountActiveByRoleAsync(Guid tenantId, UserRole role, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users
            .Where(u => u.TenantId == tenantId && u.Role == role && u.IsActive)
            .CountAsync(cancellationToken);
    }

    public async Task<PagedResult<User>> GetPagedAsync(Guid tenantId, UserRole? role, bool? isActive, string? search, DateTime? createdFrom, DateTime? createdTo, string? sortBy, bool isDescending, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Users.Include(u => u.UserProcesses).Where(u => u.TenantId == tenantId);

        if (role.HasValue)
            query = query.Where(u => u.Role == role.Value);

        if (isActive.HasValue)
            query = query.Where(u => u.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(u => u.FirstName.ToLower().Contains(s) || u.LastName.ToLower().Contains(s) || u.Email.ToLower().Contains(s));
        }

        if (createdFrom.HasValue)
        {
            var from = DateTime.SpecifyKind(createdFrom.Value.Date, DateTimeKind.Utc);
            query = query.Where(x => x.CreatedAt >= from);
        }
        if (createdTo.HasValue)
        {
            var to = DateTime.SpecifyKind(createdTo.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(x => x.CreatedAt < to);
        }

        IOrderedQueryable<User> sorted;
        switch (sortBy?.ToLowerInvariant())
        {
            case "email":
                sorted = isDescending ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email);
                break;
            case "createdat":
                sorted = isDescending ? query.OrderByDescending(u => u.CreatedAt) : query.OrderBy(u => u.CreatedAt);
                break;
            default: // lastName asc, firstName asc
                sorted = isDescending
                    ? query.OrderByDescending(u => u.LastName).ThenByDescending(u => u.FirstName)
                    : query.OrderBy(u => u.LastName).ThenBy(u => u.FirstName);
                break;
        }

        return await sorted.ToPagedResultAsync(page, pageSize, cancellationToken);
    }
}
