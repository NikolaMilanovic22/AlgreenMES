using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Orders.Infrastructure.Repositories;

public class OrderItemProcessRepository : IOrderItemProcessRepository
{
    private readonly OrdersDbContext _dbContext;

    public OrderItemProcessRepository(OrdersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<OrderItemProcess?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.OrderItemProcesses
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<OrderItemProcess?> GetByIdWithSubProcessesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.OrderItemProcesses
            .Include(p => p.SubProcesses)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<OrderItemProcess?> GetByIdWithOrderDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.OrderItemProcesses
            .Include(p => p.SubProcesses)
                .ThenInclude(sp => sp.Logs)
            .Include(p => p.ProcessLogs)
            .Include(p => p.OrderItem)
                .ThenInclude(oi => oi.Order)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<OrderItemProcess?> GetByIdWithFullDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.OrderItemProcesses
            .Include(p => p.SubProcesses)
                .ThenInclude(sp => sp.Logs)
            .Include(p => p.ProcessLogs)
            .Include(p => p.OrderItem)
                .ThenInclude(oi => oi.Order)
            // Sibling processes are needed by StartProcessWork's dependency
            // gate (it reads orderItem.Processes). Without this Include (lazy
            // loading is off) that collection is empty and DEPENDENCY_NOT_MET
            // never fires — the category-dependency sequencing guard is dead.
            .Include(p => p.OrderItem)
                .ThenInclude(oi => oi.Processes)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderItemProcess>> GetByOrderItemIdAsync(Guid orderItemId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.OrderItemProcesses
            .Include(p => p.SubProcesses)
            .Where(p => p.OrderItemId == orderItemId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrderItemProcess>> GetInProgressByProcessIdAsync(Guid processId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.OrderItemProcesses
            .Include(p => p.SubProcesses)
                .ThenInclude(sp => sp.Logs)
            .Include(p => p.ProcessLogs)
            .Where(p => p.TenantId == tenantId
                && p.ProcessId == processId
                && p.Status == Domain.Enums.ProcessStatus.InProgress)
            .ToListAsync(cancellationToken);
    }
}
