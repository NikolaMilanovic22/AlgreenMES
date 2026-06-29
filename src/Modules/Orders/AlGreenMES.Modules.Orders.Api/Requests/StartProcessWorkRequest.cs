namespace AlGreenMES.Modules.Orders.Api.Requests;

// OccurredAt: client timestamp of when the worker tapped (for offline actions
// replayed later). Null = treated as "now" on the server. ActionId: client
// idempotency key so a replayed/duplicate request applies exactly once. Both
// optional — today's callers send neither.
public record StartProcessWorkRequest(Guid UserId, DateTime? OccurredAt = null, Guid? ActionId = null);
