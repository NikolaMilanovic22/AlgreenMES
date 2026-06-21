using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Tenancy.Application.DTOs;
using AlGreenMES.Modules.Tenancy.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Tenancy.Application.Queries.GetTenantById;

public class GetTenantByIdQueryHandler : IRequestHandler<GetTenantByIdQuery, TenantDto>
{
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantPaymentRepository _paymentRepository;

    public GetTenantByIdQueryHandler(ITenantRepository tenantRepository, ITenantPaymentRepository paymentRepository)
    {
        _tenantRepository = tenantRepository;
        _paymentRepository = paymentRepository;
    }

    public async Task<TenantDto> Handle(GetTenantByIdQuery request, CancellationToken cancellationToken)
    {
        var tenant = await _tenantRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Tenant", request.Id);

        var lastPaid = await _paymentRepository.GetLastPaidAtAsync(request.Id, cancellationToken);
        var paidThrough = await _paymentRepository.GetPaidThroughAsync(request.Id, cancellationToken);

        return tenant.Adapt<TenantDto>() with
        {
            LastPaidAt = lastPaid,
            PaidThrough = paidThrough,
        };
    }
}
