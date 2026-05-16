using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Production.Domain.Entities;

namespace AlGreenMES.Modules.Production.Domain.Repositories;

public interface IProductCategoryRepository
{
    Task<ProductCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductCategory?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductCategory>> GetByIdsWithDetailsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductCategory>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task AddAsync(ProductCategory category, CancellationToken cancellationToken = default);
    void Remove(ProductCategory category);
    Task<bool> ExistsByNameAsync(string name, Guid tenantId, CancellationToken cancellationToken = default);
    Task<PagedResult<ProductCategory>> GetPagedAsync(Guid tenantId, bool? isActive, string? search, DateTime? createdFrom, DateTime? createdTo, string? sortBy, bool isDescending, int page, int pageSize, CancellationToken cancellationToken = default);
}
