using AlGreenMES.Modules.Production.Application.DTOs;
using AlGreenMES.Modules.Production.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Queries.GetStanje;

public class GetStanjeQueryHandler : IRequestHandler<GetStanjeQuery, IReadOnlyList<StanjeRowDto>>
{
    private readonly IMaterialRepository _materialRepo;
    private readonly IStockMovementRepository _stockRepo;

    public GetStanjeQueryHandler(IMaterialRepository materialRepo, IStockMovementRepository stockRepo)
    {
        _materialRepo = materialRepo;
        _stockRepo = stockRepo;
    }

    public async Task<IReadOnlyList<StanjeRowDto>> Handle(GetStanjeQuery request, CancellationToken cancellationToken)
    {
        var materials = await _materialRepo.GetByTenantIdAsync(request.TenantId, cancellationToken);
        var balances = (await _stockRepo.GetBalancesAsync(request.TenantId, cancellationToken))
            .ToDictionary(b => b.MaterialId);

        return materials
            .Where(m => m.IsActive)
            .Select(m =>
            {
                balances.TryGetValue(m.Id, out var bal);
                var qty = bal?.Quantity ?? 0m;
                var price = bal?.LatestUnitPrice ?? 0m;
                StockStatus status;
                if (qty < m.MinQuantity) status = StockStatus.IspodMin;
                else if (m.MaxQuantity > 0 && qty > m.MaxQuantity) status = StockStatus.IznadMax;
                else status = StockStatus.Ok;
                return new StanjeRowDto(
                    m.Id, m.Code, m.Name, m.Unit, m.Category,
                    m.DimensionX, m.DimensionY, m.DimensionZ,
                    qty, price, Math.Round(qty * price, 2),
                    m.MinQuantity, m.MaxQuantity, status,
                    m.Location, m.Notes);
            })
            // Status zaliha first — warnings on top.
            .OrderByDescending(r => r.Status == StockStatus.IspodMin)
            .ThenByDescending(r => r.Status == StockStatus.IznadMax)
            .ThenBy(r => r.Category)
            .ThenBy(r => r.Code)
            .ToList();
    }
}
