using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Tenancy.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Tenancy.Application.Commands.DeleteTenantPayment;

public class DeleteTenantPaymentCommandHandler : IRequestHandler<DeleteTenantPaymentCommand, Unit>
{
    private readonly ITenantPaymentRepository _paymentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteTenantPaymentCommandHandler(ITenantPaymentRepository paymentRepository, IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(DeleteTenantPaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.GetByIdAsync(request.PaymentId, cancellationToken)
            ?? throw new NotFoundException("TenantPayment", request.PaymentId);

        if (payment.TenantId != request.TenantId)
            throw new NotFoundException("TenantPayment", request.PaymentId);

        _paymentRepository.Remove(payment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
