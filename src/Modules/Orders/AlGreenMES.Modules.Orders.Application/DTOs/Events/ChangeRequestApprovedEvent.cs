namespace AlGreenMES.Modules.Orders.Application.DTOs.Events;

public record ChangeRequestApprovedEvent(
    Guid ChangeRequestId,
    Guid OrderId,
    string OrderNumber,
    Guid RequestedByUserId,
    Guid TenantId);
