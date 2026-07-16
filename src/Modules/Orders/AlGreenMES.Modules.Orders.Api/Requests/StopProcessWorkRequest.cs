namespace AlGreenMES.Modules.Orders.Api.Requests;

public record StopProcessWorkRequest(Guid UserId, DateTime? OccurredAt = null, Guid? ActionId = null);
