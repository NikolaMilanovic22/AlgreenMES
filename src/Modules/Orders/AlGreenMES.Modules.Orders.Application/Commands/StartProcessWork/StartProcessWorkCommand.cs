using AlGreenMES.Modules.Orders.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.StartProcessWork;

public record StartProcessWorkCommand(Guid OrderItemProcessId, Guid UserId, DateTime? OccurredAt = null, Guid? ActionId = null) : IRequest<OrderItemProcessDto>;
