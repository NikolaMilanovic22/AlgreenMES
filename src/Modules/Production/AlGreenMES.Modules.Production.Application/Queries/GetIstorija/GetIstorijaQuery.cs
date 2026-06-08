using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Production.Application.DTOs;
using AlGreenMES.Modules.Production.Domain.Enums;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Queries.GetIstorija;

public record GetIstorijaQuery(
    Guid TenantId,
    StockMovementType? Type,
    Guid? MaterialId,
    string? DocRef,
    DateTime? From,
    DateTime? To,
    int Page,
    int PageSize) : IRequest<PagedResult<StockMovementDto>>;
