using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Production.Domain.Entities;
using AlGreenMES.Modules.Production.Domain.Enums;

namespace AlGreenMES.Modules.Production.Domain.Repositories;

public interface IStockMovementRepository
{
    Task AddAsync(StockMovement movement, CancellationToken cancellationToken = default);

    /// <summary>Per-material running totals. Used by Stanje query.</summary>
    Task<IReadOnlyList<StockBalanceRow>> GetBalancesAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Last unit price entered for a material (any movement type). Null if no movements.</summary>
    Task<decimal?> GetLatestUnitPriceAsync(Guid tenantId, Guid materialId, CancellationToken cancellationToken = default);

    /// <summary>Current on-hand quantity per material (Inflow − Outflow). Materials with no movements omitted.</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetQuantitiesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> materialIds,
        CancellationToken cancellationToken = default);

    Task<PagedResult<StockMovement>> GetPagedAsync(
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
        CancellationToken cancellationToken = default);
}

/// <summary>Stanje row — per-material aggregate of all stock movements.</summary>
public record StockBalanceRow(Guid MaterialId, decimal Quantity, decimal LatestUnitPrice);
