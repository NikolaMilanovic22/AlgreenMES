using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Production.Domain.Entities;
using AlGreenMES.Modules.Production.Domain.Repositories;
using AlGreenMES.Modules.Production.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Production.Infrastructure.Repositories;

public class ProductCategoryRepository : IProductCategoryRepository
{
    private readonly ProductionDbContext _dbContext;

    public ProductCategoryRepository(ProductionDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ProductCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ProductCategories
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<ProductCategory?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ProductCategories
            .Include(c => c.Processes)
                .ThenInclude(p => p.Process)
            .Include(c => c.Dependencies)
                .ThenInclude(d => d.Process)
            .Include(c => c.Dependencies)
                .ThenInclude(d => d.DependsOnProcess)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ProductCategory>> GetByIdsWithDetailsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids?.Distinct().ToList() ?? new List<Guid>();
        if (idList.Count == 0) return Array.Empty<ProductCategory>();

        return await _dbContext.ProductCategories
            .Include(c => c.Processes)
                .ThenInclude(p => p.Process)
            .Include(c => c.Dependencies)
                .ThenInclude(d => d.Process)
            .Include(c => c.Dependencies)
                .ThenInclude(d => d.DependsOnProcess)
            .Where(c => idList.Contains(c.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductCategory>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ProductCategories
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(ProductCategory category, CancellationToken cancellationToken = default)
    {
        await _dbContext.ProductCategories.AddAsync(category, cancellationToken);
    }

    public void Remove(ProductCategory category)
    {
        _dbContext.ProductCategories.Remove(category);
    }

    public async Task<bool> ExistsByNameAsync(string name, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var normalizedName = name.Trim();
        return await _dbContext.ProductCategories
            .AnyAsync(c => c.Name == normalizedName && c.TenantId == tenantId, cancellationToken);
    }

    public async Task<PagedResult<ProductCategory>> GetPagedAsync(Guid tenantId, bool? isActive, string? search, DateTime? createdFrom, DateTime? createdTo, string? sortBy, bool isDescending, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ProductCategories.Where(pc => pc.TenantId == tenantId);

        if (isActive.HasValue)
            query = query.Where(pc => pc.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(pc => pc.Name.ToLower().Contains(s));
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

        IOrderedQueryable<ProductCategory> sorted;
        switch (sortBy?.ToLowerInvariant())
        {
            case "createdat":
                sorted = isDescending ? query.OrderByDescending(pc => pc.CreatedAt) : query.OrderBy(pc => pc.CreatedAt);
                break;
            default: // name asc
                sorted = isDescending ? query.OrderByDescending(pc => pc.Name) : query.OrderBy(pc => pc.Name);
                break;
        }

        return await sorted.ToPagedResultAsync(page, pageSize, cancellationToken);
    }
}
