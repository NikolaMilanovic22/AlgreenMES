namespace AlGreenMES.Modules.Orders.Application.DTOs.Events;

public record ChangeRequestCreatedEvent(
    Guid ChangeRequestId,
    Guid OrderId,
    string OrderNumber,
    string RequestType,
    Guid TenantId);
