using AlGreenMES.Modules.Tenancy.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Tenancy.Application.Queries.GetTenantPayments;

public record GetTenantPaymentsQuery(Guid TenantId) : IRequest<IReadOnlyList<TenantPaymentDto>>;
