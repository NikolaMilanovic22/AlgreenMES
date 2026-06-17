using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Tenancy.Application.DTOs;
using AlGreenMES.Modules.Tenancy.Domain.Entities;
using AlGreenMES.Modules.Tenancy.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Tenancy.Application.Commands.CreateTenantPayment;

public class CreateTenantPaymentCommandHandler : IRequestHandler<CreateTenantPaymentCommand, TenantPaymentDto>
{
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantPaymentRepository _paymentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTenantPaymentCommandHandler(
        ITenantRepository tenantRepository,
        ITenantPaymentRepository paymentRepository,
        IUnitOfWork unitOfWork)
    {
        _tenantRepository = tenantRepository;
        _paymentRepository = paymentRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<TenantPaymentDto> Handle(CreateTenantPaymentCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _tenantRepository.GetByIdAsync(request.TenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant", request.TenantId);

        var payment = TenantPayment.Create(
            tenant.Id,
            request.PeriodStart,
            request.PeriodEnd,
            request.Amount,
            request.Currency,
            request.PaidAt,
            request.InvoiceNumber,
            request.Notes);

        await _paymentRepository.AddAsync(payment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return payment.Adapt<TenantPaymentDto>();
    }
}
