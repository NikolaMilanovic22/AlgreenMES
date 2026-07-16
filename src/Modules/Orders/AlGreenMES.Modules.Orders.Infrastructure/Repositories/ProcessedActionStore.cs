using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Orders.Infrastructure.Repositories;

public class ProcessedActionStore : IProcessedActionStore
{
    private readonly OrdersDbContext _dbContext;

    public ProcessedActionStore(OrdersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsAsync(Guid actionId, CancellationToken cancellationToken = default)
    {
        // ActionId is globally unique (client GUID); the tenant query filter
        // additionally scopes the read to the current tenant, which is correct
        // since the row was written under the acting tenant.
        return _dbContext.ProcessedActions.AnyAsync(a => a.ActionId == actionId, cancellationToken);
    }

    public void Record(Guid tenantId, Guid actionId, string actionType)
    {
        _dbContext.ProcessedActions.Add(ProcessedAction.Create(tenantId, actionId, actionType));
    }
}
