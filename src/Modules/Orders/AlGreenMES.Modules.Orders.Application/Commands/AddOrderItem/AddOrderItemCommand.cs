using AlGreenMES.Modules.Orders.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.AddOrderItem;

public record AddOrderItemCommand(
    Guid OrderId,
    Guid ProductCategoryId,
    string? ProductName,
    int Quantity,
    string? Notes) : IRequest<OrderDetailDto>;
