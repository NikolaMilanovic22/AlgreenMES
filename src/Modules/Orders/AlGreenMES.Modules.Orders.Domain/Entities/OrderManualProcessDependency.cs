using AlGreenMES.BuildingBlocks.Common.Entities;
using AlGreenMES.BuildingBlocks.Common.Exceptions;

namespace AlGreenMES.Modules.Orders.Domain.Entities;

/// <summary>
/// Dependency edge between two manual processes on an order. Mirrors
/// ProductCategoryDependency but scoped to a specific order — used when the
/// order's OrderType has AllowsManualProcesses=true.
/// </summary>
public class OrderManualProcessDependency : AuditableEntity
{
    public Guid OrderId { get; private set; }
    public Guid ProcessId { get; private set; }
    public Guid DependsOnProcessId { get; private set; }

    private OrderManualProcessDependency() { }

    public static OrderManualProcessDependency Create(Guid tenantId, Guid orderId, Guid processId, Guid dependsOnProcessId)
    {
        if (processId == dependsOnProcessId)
            throw new DomainException("INVALID_DEPENDENCY", "A process cannot depend on itself.");

        return new OrderManualProcessDependency
        {
            TenantId = tenantId,
            OrderId = orderId,
            ProcessId = processId,
            DependsOnProcessId = dependsOnProcessId,
        };
    }
}
