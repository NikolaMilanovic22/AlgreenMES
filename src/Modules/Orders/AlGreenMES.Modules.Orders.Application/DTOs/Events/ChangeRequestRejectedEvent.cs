namespace AlGreenMES.Modules.Orders.Application.DTOs.Events;

public record ChangeRequestRejectedEvent(
    Guid ChangeRequestId,
    Guid OrderId,
    string OrderNumber,
    Guid RequestedByUserId,
    string? RejectionNote,
    Guid TenantId);
