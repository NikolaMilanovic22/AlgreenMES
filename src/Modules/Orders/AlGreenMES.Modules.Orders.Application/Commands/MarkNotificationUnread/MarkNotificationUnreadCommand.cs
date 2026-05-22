using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.MarkNotificationUnread;

public record MarkNotificationUnreadCommand(Guid Id, Guid UserId) : IRequest<Unit>;
