using AlGreenMES.Modules.Orders.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.CreateOrderType;

public record CreateOrderTypeCommand(
    Guid TenantId,
    string? Code,
    string Name,
    bool AllowsManualProcesses) : IRequest<OrderTypeDto>;
