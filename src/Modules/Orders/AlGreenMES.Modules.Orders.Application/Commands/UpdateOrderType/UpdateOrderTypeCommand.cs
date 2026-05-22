using AlGreenMES.Modules.Orders.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.UpdateOrderType;

public record UpdateOrderTypeCommand(
    Guid Id,
    string Name,
    bool AllowsManualProcesses,
    bool IsActive) : IRequest<OrderTypeDto>;
