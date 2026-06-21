using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Tenancy.Application.DTOs;
using AlGreenMES.Modules.Tenancy.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Tenancy.Application.Queries.GetTenants;

public class GetTenantsQueryHandler : IRequestHandler<GetTenantsQuery, PagedResult<TenantDto>>
{
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantPaymentRepository _paymentRepository;

    public GetTenantsQueryHandler(ITenantRepository tenantRepository, ITenantPaymentRepository paymentRepository)
    {
        _tenantRepository = tenantRepository;
        _paymentRepository = paymentRepository;
    }

    public async Task<PagedResult<TenantDto>> Handle(GetTenantsQuery request, CancellationToken cancellationToken)
    {
        var result = await _tenantRepository.GetPagedAsync(
            request.IsActive, request.Search,
            request.GetCreatedFromUtc(), request.GetCreatedToUtc(),
            request.SortBy, request.IsDescending,
            request.GetPage(), request.GetPageSize(), cancellationToken);

        var lastPaidByTenant = await _paymentRepository.GetLastPaidAtByTenantAsync(cancellationToken);
        var paidThroughByTenant = await _paymentRepository.GetPaidThroughByTenantAsync(cancellationToken);

        return result.MapItems(t =>
        {
            var dto = t.Adapt<TenantDto>();
            lastPaidByTenant.TryGetValue(t.Id, out var lastPaid);
            paidThroughByTenant.TryGetValue(t.Id, out var paidThrough);
            return dto with
            {
                LastPaidAt = lastPaid == default ? null : lastPaid,
                PaidThrough = paidThrough == default ? null : paidThrough,
            };
        });
    }
}
