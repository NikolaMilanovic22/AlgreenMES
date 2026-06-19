using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Identity.Domain.Entities;

namespace AlGreenMES.Modules.Identity.Domain.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> GetByIdWithProcessesAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>
    /// Variant of <see cref="GetByIdWithProcessesAsync"/> that bypasses the
    /// tenant filter so it resolves tenantless SuperAdmins. Used by the
    /// Update/Delete user-management handlers so they can locate SA targets
    /// and apply peer-SA protection — the handlers then enforce the cross-
    /// tenant boundary explicitly. Without this the tenant filter masks SAs
    /// from an Admin's view and the auth check falls through to 404 instead
    /// of 403 (Saša 19.06.2026).
    /// </summary>
    Task<User?> GetByIdWithProcessesIgnoreFiltersAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>For pre-auth flows (refresh) where the JWT and tenant scope are not yet established.</summary>
    Task<User?> GetByIdIgnoreFiltersAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> GetByEmailAsync(string email, Guid tenantId, CancellationToken cancellationToken = default);
    Task<User?> GetByEmailWithProcessesAsync(string email, Guid tenantId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Lookup a user by email IGNORING tenant scope. Used only by the
    /// SuperAdmin cross-tenant login path — callers must verify the
    /// returned user actually IS a SuperAdmin before honoring the
    /// cross-tenant intent. Returns null when no match exists.
    /// </summary>
    Task<User?> GetByEmailAcrossTenantsAsync(string email, CancellationToken cancellationToken = default);
    /// <summary>
    /// Every SuperAdmin user across every tenant. Used by the
    /// "Sistem administratori" tab — SuperAdmin-only at the controller.
    /// </summary>
    Task<IReadOnlyList<User>> GetAllSuperAdminsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task AddAsync(User user, CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailAsync(string email, Guid tenantId, CancellationToken cancellationToken = default);
    Task<PagedResult<User>> GetPagedAsync(Guid tenantId, UserRole? role, bool? isActive, string? search, DateTime? createdFrom, DateTime? createdTo, string? sortBy, bool isDescending, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> GetByProcessIdAsync(Guid processId, Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> GetDepartmentUsersWithProcessesAsync(Guid tenantId, CancellationToken cancellationToken = default);
    /// <summary>Count active users of a given role in a tenant. Used by last-Admin guards (Sprint 3.0 F-1/F-2).</summary>
    Task<int> CountActiveByRoleAsync(Guid tenantId, UserRole role, CancellationToken cancellationToken = default);
    void Delete(User user);
}
