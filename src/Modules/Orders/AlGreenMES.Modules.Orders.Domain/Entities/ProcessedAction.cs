using AlGreenMES.BuildingBlocks.Common.Entities;

namespace AlGreenMES.Modules.Orders.Domain.Entities;

/// <summary>
/// Idempotency ledger for tablet workflow actions. The tablet generates an
/// ActionId (GUID) at the moment the worker taps; that same id rides the
/// original request and every offline replay. The first time we process an
/// ActionId we record it here; any later request carrying the same id is a
/// duplicate (a lost-response retry or a queued offline replay) and is
/// short-circuited to a no-op returning the current state — so an action
/// applies exactly once, never twice. The unique index on ActionId is the
/// hard guarantee; the handler-level check is the fast path.
/// </summary>
public class ProcessedAction : TenantEntity
{
    public Guid ActionId { get; private set; }
    public string ActionType { get; private set; } = string.Empty;
    public DateTime ProcessedAt { get; private set; }

    private ProcessedAction()
    {
    }

    public static ProcessedAction Create(Guid tenantId, Guid actionId, string actionType)
    {
        return new ProcessedAction
        {
            TenantId = tenantId,
            ActionId = actionId,
            ActionType = actionType,
            ProcessedAt = DateTime.UtcNow,
        };
    }
}
