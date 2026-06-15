using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Production.Application.DTOs;
using AlGreenMES.Modules.Production.Domain.Enums;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Queries.GetStockHistory;

public record GetStockHistoryQuery(
    Guid TenantId,
    StockMovementType? Type,
    Guid? MaterialId,
    string? DocRef,
    DateTime? From,
    DateTime? To,
    int Page,
    int PageSize,
    string? SortBy = null,
    string? SortDirection = null,
    string? Category = null) : IRequest<PagedResult<StockMovementDto>>;
