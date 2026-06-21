using AlGreenMES.BuildingBlocks.Common.Pagination;
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
        // Saša 18.06.2026: only count payments whose period has actually
        // STARTED — a pre-paid payment (periodStart in the future) doesn't
        // promote the tenant to "Plaćeno" until its period kicks in.
        // Without this filter, recording a payment for next month makes
        // the tenant look paid-up today.
        var today = DateTime.UtcNow.Date;
        return await _dbContext.TenantPayments
            .Where(p => p.TenantId == tenantId && p.PeriodStart <= today)
            .OrderByDescending(p => p.PeriodEnd)
            .Select(p => (DateTime?)p.PeriodEnd)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, DateTime>> GetPaidThroughByTenantAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;
        var rows = await _dbContext.TenantPayments
            .Where(p => p.PeriodStart <= today)
            .GroupBy(p => p.TenantId)
            .Select(g => new { TenantId = g.Key, PaidThrough = g.Max(p => p.PeriodEnd) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.TenantId, r => r.PaidThrough);
    }

    public async Task<PagedResult<TenantPaymentRow>> GetAllPagedAsync(
        Guid? tenantId,
        DateTime? paidFrom,
        DateTime? paidTo,
        string? currency,
        string? sortBy,
        bool isDescending,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query =
            from p in _dbContext.TenantPayments
            join t in _dbContext.Tenants on p.TenantId equals t.Id
            select new { p, t };

        if (tenantId.HasValue)
            query = query.Where(x => x.p.TenantId == tenantId.Value);

        if (paidFrom.HasValue)
        {
            var from = DateTime.SpecifyKind(paidFrom.Value.Date, DateTimeKind.Utc);
            query = query.Where(x => x.p.PaidAt >= from);
        }
        if (paidTo.HasValue)
        {
            var to = DateTime.SpecifyKind(paidTo.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(x => x.p.PaidAt < to);
        }
        if (!string.IsNullOrWhiteSpace(currency))
        {
            var c = currency.Trim().ToUpperInvariant();
            query = query.Where(x => x.p.Currency == c);
        }

        query = (sortBy?.ToLowerInvariant()) switch
        {
            "tenantname" => isDescending ? query.OrderByDescending(x => x.t.Name) : query.OrderBy(x => x.t.Name),
            "amount"     => isDescending ? query.OrderByDescending(x => x.p.Amount) : query.OrderBy(x => x.p.Amount),
            "periodstart"=> isDescending ? query.OrderByDescending(x => x.p.PeriodStart) : query.OrderBy(x => x.p.PeriodStart),
            _            => isDescending ? query.OrderBy(x => x.p.PaidAt) : query.OrderByDescending(x => x.p.PaidAt),
        };

        var projected = query.Select(x => new TenantPaymentRow(
            x.p.Id,
            x.p.TenantId,
            x.t.Name,
            x.t.Code,
            x.p.PeriodStart,
            x.p.PeriodEnd,
            x.p.Amount,
            x.p.Currency,
            x.p.PaidAt,
            x.p.InvoiceNumber,
            x.p.Notes,
            x.p.CreatedAt));

        return await projected.ToPagedResultAsync(page, pageSize, cancellationToken);
    }
}
