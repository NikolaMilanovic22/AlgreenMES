namespace AlGreenMES.Modules.Orders.Domain.Repositories;

/// <summary>
/// Idempotency ledger for tablet workflow actions. See
/// <see cref="Entities.ProcessedAction"/>. Lets a handler skip re-applying an
/// action it has already processed (a lost-response retry or an offline replay
/// carrying the same client ActionId).
/// </summary>
public interface IProcessedActionStore
{
    /// <summary>True if this ActionId has already been processed.</summary>
    Task<bool> ExistsAsync(Guid actionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stage a ProcessedAction for the given id. Persisted by the caller's
    /// unit-of-work SaveChanges so it commits in the same transaction as the
    /// action it records.
    /// </summary>
    void Record(Guid tenantId, Guid actionId, string actionType);
}
