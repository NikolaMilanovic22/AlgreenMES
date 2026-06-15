using AlGreenMES.Modules.Production.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Queries.GetStockBalances;

public record GetStockBalancesQuery(Guid TenantId) : IRequest<IReadOnlyList<StockBalanceRowDto>>;
