namespace AlGreenMES.Modules.Orders.Api.Requests;

// Optional body for the complete-process endpoint. The endpoint historically
// took no body, so this is allowed to be empty (EmptyBodyBehavior.Allow on the
// action). OccurredAt = client timestamp for offline actions replayed later.
public record CompleteProcessRequest(DateTime? OccurredAt = null, Guid? ActionId = null);
