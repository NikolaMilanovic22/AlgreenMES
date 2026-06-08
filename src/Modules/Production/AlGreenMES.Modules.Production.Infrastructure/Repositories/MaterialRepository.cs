using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Production.Domain.Entities;
using AlGreenMES.Modules.Production.Domain.Repositories;
using AlGreenMES.Modules.Production.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Production.Infrastructure.Repositories;

public class MaterialRepository : IMaterialRepository
{
    private readonly ProductionDbContext _dbContext;

    public MaterialRepository(ProductionDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Material?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.Materials.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Material>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Materials
            .Where(m => m.TenantId == tenantId)
            .OrderBy(m => m.Code)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Material>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0) return Array.Empty<Material>();
        return await _dbContext.Materials
            .Where(m => idList.Contains(m.Id))
            .ToListAsync(cancellationToken);
    }

    public Task AddAsync(Material material, CancellationToken cancellationToken = default) =>
        _dbContext.Materials.AddAsync(material, cancellationToken).AsTask();

    public Task<bool> ExistsByKodAsync(string code, Guid tenantId, Guid? excludingId, CancellationToken cancellationToken = default)
    {
        var trimmed = code.Trim();
        var q = _dbContext.Materials.Where(m => m.TenantId == tenantId && m.Code == trimmed);
        if (excludingId.HasValue) q = q.Where(m => m.Id != excludingId.Value);
        return q.AnyAsync(cancellationToken);
    }

    public async Task<PagedResult<Material>> GetPagedAsync(
        Guid tenantId, bool? isActive, string? category, string? search,
        string? sortBy, bool isDescending, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Materials.Where(m => m.TenantId == tenantId);
        if (isActive.HasValue) query = query.Where(m => m.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(m => m.Category == category);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(m => m.Code.ToLower().Contains(s) || m.Name.ToLower().Contains(s));
        }

        IOrderedQueryable<Material> sorted = (sortBy?.ToLowerInvariant()) switch
        {
            "name" => isDescending ? query.OrderByDescending(m => m.Name) : query.OrderBy(m => m.Name),
            "category" => isDescending ? query.OrderByDescending(m => m.Category) : query.OrderBy(m => m.Category),
            "createdat" => isDescending ? query.OrderByDescending(m => m.CreatedAt) : query.OrderBy(m => m.CreatedAt),
            _ => isDescending ? query.OrderByDescending(m => m.Code) : query.OrderBy(m => m.Code),
        };

        return await sorted.ToPagedResultAsync(page, pageSize, cancellationToken);
    }
}
