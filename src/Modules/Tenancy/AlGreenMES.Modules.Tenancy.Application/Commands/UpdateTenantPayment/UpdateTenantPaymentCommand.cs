using AlGreenMES.Modules.Tenancy.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Tenancy.Application.Commands.UpdateTenantPayment;

public record UpdateTenantPaymentCommand(
    Guid TenantId,
    Guid PaymentId,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    decimal Amount,
    string Currency,
    DateTime PaidAt,
    string? InvoiceNumber,
    string? Notes) : IRequest<TenantPaymentDto>;
