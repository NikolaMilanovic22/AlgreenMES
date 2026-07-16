namespace AlGreenMES.Modules.Orders.Api.Requests;

public record CompleteSubProcessRequest(Guid UserId, DateTime? OccurredAt = null, Guid? ActionId = null);
