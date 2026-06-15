using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Production.Domain.Entities;

namespace AlGreenMES.Modules.Production.Domain.Repositories;

public interface IMaterialRepository
{
    Task<Material?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Material>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Material>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task AddAsync(Material material, CancellationToken cancellationToken = default);
    Task<bool> ExistsByKodAsync(string code, Guid tenantId, Guid? excludingId, CancellationToken cancellationToken = default);
    Task<PagedResult<Material>> GetPagedAsync(
        Guid tenantId, bool? isActive, string? category, string? search,
        string? sortBy, bool isDescending, int page, int pageSize,
        CancellationToken cancellationToken = default);
}
