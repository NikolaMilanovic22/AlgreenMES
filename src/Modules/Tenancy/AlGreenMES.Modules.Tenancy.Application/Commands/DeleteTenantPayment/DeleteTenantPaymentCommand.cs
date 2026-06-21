using MediatR;

namespace AlGreenMES.Modules.Tenancy.Application.Commands.DeleteTenantPayment;

public record DeleteTenantPaymentCommand(Guid TenantId, Guid PaymentId) : IRequest<Unit>;
