using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Tenancy.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Tenancy.Application.Queries.GetAllPayments;

public record GetAllPaymentsQuery(
    Guid? TenantId,
    DateTime? PaidFrom,
    DateTime? PaidTo,
    string? Currency,
    int Page = 1,
    int PageSize = 50,
    string? SortBy = null,
    string? SortDirection = null) : IRequest<PagedResult<AllTenantPaymentDto>>;
