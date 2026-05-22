using AlGreenMES.BuildingBlocks.Common.Entities;
using AlGreenMES.Modules.Production.Domain.Enums;

namespace AlGreenMES.Modules.Orders.Domain.Entities;

/// <summary>
/// Per-order list of processes used when the order's OrderType has
/// AllowsManualProcesses=true. Replaces the per-item product-category processes
/// for orders of that type. Sequence + optional complexity hint.
/// </summary>
public class OrderManualProcess : AuditableEntity
{
    public Guid OrderId { get; private set; }
    public Guid ProcessId { get; private set; }
    public int SequenceOrder { get; private set; }
    public ComplexityType? DefaultComplexity { get; private set; }

    private OrderManualProcess() { }

    public static OrderManualProcess Create(Guid tenantId, Guid orderId, Guid processId, int sequenceOrder, ComplexityType? complexity)
    {
        return new OrderManualProcess
        {
            TenantId = tenantId,
            OrderId = orderId,
            ProcessId = processId,
            SequenceOrder = sequenceOrder,
            DefaultComplexity = complexity,
        };
    }
}
