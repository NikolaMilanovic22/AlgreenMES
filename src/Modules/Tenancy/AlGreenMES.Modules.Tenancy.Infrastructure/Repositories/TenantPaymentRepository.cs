using AlGreenMES.Modules.Tenancy.Domain.Entities;
using AlGreenMES.Modules.Tenancy.Domain.Repositories;
using AlGreenMES.Modules.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Tenancy.Infrastructure.Repositories;

public class TenantPaymentRepository : ITenantPaymentRepository
{
    private readonly TenancyDbContext _dbContext;

    public TenantPaymentRepository(TenancyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<TenantPayment>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantPayments
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.PaidAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<TenantPayment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantPayments
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task AddAsync(TenantPayment payment, CancellationToken cancellationToken = default)
    {
        await _dbContext.TenantPayments.AddAsync(payment, cancellationToken);
    }

    public void Remove(TenantPayment payment)
    {
        _dbContext.TenantPayments.Remove(payment);
    }

    public async Task<DateTime?> GetLastPaidAtAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantPayments
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.PaidAt)
            .Select(p => (DateTime?)p.PaidAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, DateTime>> GetLastPaidAtByTenantAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.TenantPayments
            .GroupBy(p => p.TenantId)
            .Select(g => new { TenantId = g.Key, LastPaidAt = g.Max(p => p.PaidAt) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.TenantId, r => r.LastPaidAt);
    }

    public async Task<DateTime?> GetPaidThroughAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantPayments
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.PeriodEnd)
            .Select(p => (DateTime?)p.PeriodEnd)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, DateTime>> GetPaidThroughByTenantAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.TenantPayments
            .GroupBy(p => p.TenantId)
            .Select(g => new { TenantId = g.Key, PaidThrough = g.Max(p => p.PeriodEnd) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.TenantId, r => r.PaidThrough);
    }
}
