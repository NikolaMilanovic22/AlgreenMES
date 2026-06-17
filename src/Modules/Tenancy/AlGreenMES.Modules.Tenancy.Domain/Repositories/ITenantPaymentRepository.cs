using AlGreenMES.Modules.Tenancy.Domain.Entities;

namespace AlGreenMES.Modules.Tenancy.Domain.Repositories;

public interface ITenantPaymentRepository
{
    Task<IReadOnlyList<TenantPayment>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<TenantPayment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(TenantPayment payment, CancellationToken cancellationToken = default);
    void Remove(TenantPayment payment);

    /// <summary>Most recent PaidAt across all payments for the tenant, or null if none.</summary>
    Task<DateTime?> GetLastPaidAtAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Latest PaidAt for every tenant in one round-trip — used by the SA tenants list to render the "Poslednja uplata" column without N+1 queries.</summary>
    Task<IReadOnlyDictionary<Guid, DateTime>> GetLastPaidAtByTenantAsync(CancellationToken cancellationToken = default);

    /// <summary>Max PeriodEnd across all payments for the tenant — the date through which they're paid up. Null if no payments yet.</summary>
    Task<DateTime?> GetPaidThroughAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Max PeriodEnd per tenant in one round-trip — used to flag overdue rows on the SA tenants list.</summary>
    Task<IReadOnlyDictionary<Guid, DateTime>> GetPaidThroughByTenantAsync(CancellationToken cancellationToken = default);
}
