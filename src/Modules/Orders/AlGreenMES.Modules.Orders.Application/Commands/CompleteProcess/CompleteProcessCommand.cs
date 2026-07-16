using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.CompleteProcess;

public record CompleteProcessCommand(Guid OrderItemProcessId, DateTime? OccurredAt = null, Guid? ActionId = null) : IRequest<Unit>;
