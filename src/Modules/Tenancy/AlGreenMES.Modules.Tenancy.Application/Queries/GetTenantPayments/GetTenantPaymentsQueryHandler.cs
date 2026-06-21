using AlGreenMES.Modules.Tenancy.Application.DTOs;
using AlGreenMES.Modules.Tenancy.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Tenancy.Application.Queries.GetTenantPayments;

public class GetTenantPaymentsQueryHandler : IRequestHandler<GetTenantPaymentsQuery, IReadOnlyList<TenantPaymentDto>>
{
    private readonly ITenantPaymentRepository _paymentRepository;

    public GetTenantPaymentsQueryHandler(ITenantPaymentRepository paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public async Task<IReadOnlyList<TenantPaymentDto>> Handle(GetTenantPaymentsQuery request, CancellationToken cancellationToken)
    {
        var payments = await _paymentRepository.GetByTenantAsync(request.TenantId, cancellationToken);
        return payments.Select(p => p.Adapt<TenantPaymentDto>()).ToList();
    }
}
