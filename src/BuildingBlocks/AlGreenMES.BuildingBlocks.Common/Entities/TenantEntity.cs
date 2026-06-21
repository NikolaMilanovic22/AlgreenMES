namespace AlGreenMES.BuildingBlocks.Common.Entities;

/// <summary>
/// Base entity with tenant isolation support.
///
/// TenantId is nullable from the schema level (Milos 16.06.2026) so that
/// SuperAdmin users — the only tenantless rows in the system — can sit in
/// the same `users` table without violating a NOT NULL constraint. Every
/// other entity has a non-null TenantId in practice; the nullability is
/// only used by User rows where Role = SuperAdmin. HasQueryFilter
/// expressions `e.TenantId == currentTenantId` continue to work because
/// SQL `NULL = X` is false, which correctly filters tenantless rows OUT
/// of tenant-scoped queries (so SuperAdmins never leak into /api/users
/// listings, /api/orders, etc.).
/// </summary>
public abstract class TenantEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// Non-null accessor for entities where the tenant is guaranteed by the
    /// domain (everything except SuperAdmin User rows). Throws when called on
    /// a SuperAdmin User by accident — that fail-loud is the whole point of
    /// having this rather than scattering <c>TenantId!.Value</c> around.
    /// </summary>
    public Guid TenantIdRequired =>
        TenantId ?? throw new InvalidOperationException(
            $"{GetType().Name} has no tenant; this property is only legal for SuperAdmin User rows.");

    protected TenantEntity()
    {
    }

    protected TenantEntity(Guid tenantId)
    {
        TenantId = tenantId;
    }
}
