namespace AlGreenMES.Modules.Orders.Api.Requests;

public record StartSubProcessRequest(Guid UserId, DateTime? OccurredAt = null, Guid? ActionId = null);
