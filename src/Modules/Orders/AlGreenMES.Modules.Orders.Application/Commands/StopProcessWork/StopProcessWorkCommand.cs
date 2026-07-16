using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.StopProcessWork;

public record StopProcessWorkCommand(Guid OrderItemProcessId, Guid UserId, DateTime? OccurredAt = null, Guid? ActionId = null) : IRequest<Unit>;
