using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Tenancy.Application.DTOs;
using AlGreenMES.Modules.Tenancy.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Tenancy.Application.Commands.UpdateTenantPayment;

public class UpdateTenantPaymentCommandHandler : IRequestHandler<UpdateTenantPaymentCommand, TenantPaymentDto>
{
    private readonly ITenantPaymentRepository _paymentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateTenantPaymentCommandHandler(ITenantPaymentRepository paymentRepository, IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<TenantPaymentDto> Handle(UpdateTenantPaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.GetByIdAsync(request.PaymentId, cancellationToken)
            ?? throw new NotFoundException("TenantPayment", request.PaymentId);

        if (payment.TenantId != request.TenantId)
            throw new NotFoundException("TenantPayment", request.PaymentId);

        payment.Update(
            request.PeriodStart,
            request.PeriodEnd,
            request.Amount,
            request.Currency,
            request.PaidAt,
            request.InvoiceNumber,
            request.Notes);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return payment.Adapt<TenantPaymentDto>();
    }
}
