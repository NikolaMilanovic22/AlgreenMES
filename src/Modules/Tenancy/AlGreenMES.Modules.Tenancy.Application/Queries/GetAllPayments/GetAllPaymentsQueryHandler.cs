using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Tenancy.Application.DTOs;
using AlGreenMES.Modules.Tenancy.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Tenancy.Application.Queries.GetAllPayments;

public class GetAllPaymentsQueryHandler : IRequestHandler<GetAllPaymentsQuery, PagedResult<AllTenantPaymentDto>>
{
    private readonly ITenantPaymentRepository _paymentRepository;

    public GetAllPaymentsQueryHandler(ITenantPaymentRepository paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public async Task<PagedResult<AllTenantPaymentDto>> Handle(GetAllPaymentsQuery request, CancellationToken cancellationToken)
    {
        var paged = await _paymentRepository.GetAllPagedAsync(
            request.TenantId,
            request.PaidFrom,
            request.PaidTo,
            request.Currency,
            request.SortBy,
            string.Equals(request.SortDirection, "desc", StringComparison.OrdinalIgnoreCase),
            request.Page,
            request.PageSize,
            cancellationToken);

        return paged.MapItems(r => new AllTenantPaymentDto(
            r.Id,
            r.TenantId,
            r.TenantName,
            r.TenantCode,
            r.PeriodStart,
            r.PeriodEnd,
            r.Amount,
            r.Currency,
            r.PaidAt,
            r.InvoiceNumber,
            r.Notes,
            r.CreatedAt));
    }
}
