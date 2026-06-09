using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Production.Domain.Entities;
using AlGreenMES.Modules.Production.Domain.Enums;
using AlGreenMES.Modules.Production.Domain.Repositories;
using AlGreenMES.Modules.Production.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Production.Infrastructure.Repositories;

public class StockMovementRepository : IStockMovementRepository
{
    private readonly ProductionDbContext _dbContext;

    public StockMovementRepository(ProductionDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(StockMovement movement, CancellationToken cancellationToken = default) =>
        _dbContext.StockMovements.AddAsync(movement, cancellationToken).AsTask();

    public async Task<IReadOnlyList<StockBalanceRow>> GetBalancesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Pull all movements for the tenant (typically small enough — and the
        // Stanje page wants everything anyway). Compute Stanje + last-price
        // per material in-memory; clearer than a DISTINCT ON in LINQ.
        var rows = await _dbContext.StockMovements
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.MovementDate).ThenBy(s => s.CreatedAt)
            .Select(s => new { s.MaterialId, s.Type, s.Quantity, s.UnitPrice })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.MaterialId)
            .Select(g =>
            {
                var qty = g.Sum(r => r.Type == StockMovementType.Inflow ? r.Quantity : -r.Quantity);
                // Latest by enumeration order (already sorted ascending) =>
                // last item is most recent. Saša 08.06.2026: always use last
                // entered price for Izlaz (no FIFO/LIFO in v1).
                var latestPrice = g.Last().UnitPrice;
                return new StockBalanceRow(g.Key, qty, latestPrice);
            })
            .ToList();
    }

    public async Task<decimal?> GetLatestUnitPriceAsync(Guid tenantId, Guid materialId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.StockMovements
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.MaterialId == materialId)
            .OrderByDescending(s => s.MovementDate)
            .ThenByDescending(s => s.CreatedAt)
            .Select(s => (decimal?)s.UnitPrice)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetQuantitiesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> materialIds,
        CancellationToken cancellationToken = default)
    {
        var ids = materialIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, decimal>();

        var rows = await _dbContext.StockMovements
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && ids.Contains(s.MaterialId))
            .GroupBy(s => s.MaterialId)
            .Select(g => new
            {
                MaterialId = g.Key,
                Quantity = g.Sum(s => s.Type == StockMovementType.Inflow ? s.Quantity : -s.Quantity),
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.MaterialId, r => r.Quantity);
    }

    public async Task<PagedResult<StockMovement>> GetPagedAsync(
        Guid tenantId,
        StockMovementType? type,
        Guid? materialId,
        string? docRef,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        string? sortBy = null,
        string? sortDirection = null,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        var q = _dbContext.StockMovements
            .Include(s => s.Material)
            .Include(s => s.Process)
            .Where(s => s.TenantId == tenantId);

        if (type.HasValue) q = q.Where(s => s.Type == type.Value);
        if (materialId.HasValue) q = q.Where(s => s.MaterialId == materialId.Value);
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(s => s.Material.Category == category);
        if (!string.IsNullOrWhiteSpace(docRef))
        {
            var ref_ = docRef.ToLower();
            q = q.Where(s => s.DocumentReference.ToLower().Contains(ref_));
        }
        if (from.HasValue)
        {
            var f = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
            q = q.Where(s => s.MovementDate >= f);
        }
        if (to.HasValue)
        {
            var t = DateTime.SpecifyKind(to.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            q = q.Where(s => s.MovementDate <= t);
        }

        var desc = !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<StockMovement> ordered = (sortBy?.ToLowerInvariant()) switch
        {
            "type"              => desc ? q.OrderByDescending(s => s.Type)                : q.OrderBy(s => s.Type),
            "materialcode"      => desc ? q.OrderByDescending(s => s.Material.Code)       : q.OrderBy(s => s.Material.Code),
            "materialname"      => desc ? q.OrderByDescending(s => s.Material.Name)       : q.OrderBy(s => s.Material.Name),
            "quantity"          => desc ? q.OrderByDescending(s => s.Quantity)            : q.OrderBy(s => s.Quantity),
            "unitprice"         => desc ? q.OrderByDescending(s => s.UnitPrice)           : q.OrderBy(s => s.UnitPrice),
            "totalprice"        => desc ? q.OrderByDescending(s => s.TotalPrice)          : q.OrderBy(s => s.TotalPrice),
            "documentreference" => desc ? q.OrderByDescending(s => s.DocumentReference)   : q.OrderBy(s => s.DocumentReference),
            "category"          => desc ? q.OrderByDescending(s => s.Material.Category)   : q.OrderBy(s => s.Material.Category),
            _                   => desc ? q.OrderByDescending(s => s.MovementDate)        : q.OrderBy(s => s.MovementDate),
        };

        return await ordered.ThenByDescending(s => s.CreatedAt)
                            .ToPagedResultAsync(page, pageSize, cancellationToken);
    }
}
