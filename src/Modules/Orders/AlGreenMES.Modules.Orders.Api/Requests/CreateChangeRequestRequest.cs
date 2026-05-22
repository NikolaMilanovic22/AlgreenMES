using AlGreenMES.Modules.Orders.Domain.Enums;

namespace AlGreenMES.Modules.Orders.Api.Requests;

public record CreateChangeRequestRequest(
    Guid OrderId,
    Guid RequestedByUserId,
    ChangeRequestType RequestType,
    string Description);
