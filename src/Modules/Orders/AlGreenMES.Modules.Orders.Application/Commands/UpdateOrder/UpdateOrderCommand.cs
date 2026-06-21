using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Production.Domain.Enums;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.UpdateOrder;

public record UpdateOrderItemInput(Guid ProductCategoryId, string? ProductName, int Quantity, string? Notes);
public record UpdateOrderComplexityInput(Guid ItemId, Guid ProcessId, ComplexityType Complexity);
public record UpdateOrderSpecialRequestAdd(Guid ItemId, Guid SpecialRequestTypeId);
public record UpdateOrderSpecialRequestRemove(Guid ItemId, Guid SpecialRequestId);

public record UpdateOrderCommand(
    Guid Id,
    string? OrderNumber,
    DateTime? DeliveryDate,
    string? Notes,
    int? CustomWarningDays,
    int? CustomCriticalDays,
    List<UpdateOrderItemInput>? AddItems = null,
    List<Guid>? RemoveItemIds = null,
    List<UpdateOrderComplexityInput>? ComplexityOverrides = null,
    List<UpdateOrderSpecialRequestAdd>? AddSpecialRequests = null,
    List<UpdateOrderSpecialRequestRemove>? RemoveSpecialRequests = null) : IRequest<OrderDto>;
