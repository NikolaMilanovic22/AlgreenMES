using AlGreenMES.BuildingBlocks.Common.Entities;
using AlGreenMES.BuildingBlocks.Common.Exceptions;

// Sub-namespace so the class name doesn't shadow the existing enum
// `Domain.Enums.OrderType` in files that import `Domain.Entities`. Consumers
// that need this entity import this namespace explicitly.
namespace AlGreenMES.Modules.Orders.Domain.Entities.OrderTypes;

/// <summary>
/// Per-tenant catalog of order classifications (Standard, Repair, Complaint,
/// Rework, plus any custom types). When AllowsManualProcesses is true, orders
/// of this type can carry a hand-picked list of processes + dependencies that
/// override the items' product-category processes (used for one-off jobs like
/// reklamiranje where the standard pipeline doesn't apply).
/// </summary>
public class OrderType : AuditableEntity
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public bool AllowsManualProcesses { get; private set; }
    public bool IsActive { get; private set; } = true;

    private OrderType()
    {
    }

    public static OrderType Create(Guid tenantId, string code, string name, bool allowsManualProcesses = false)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("INVALID_CODE", "Order type code is required.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("INVALID_NAME", "Order type name is required.");

        return new OrderType
        {
            TenantId = tenantId,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            AllowsManualProcesses = allowsManualProcesses,
        };
    }

    public void Update(string name, bool allowsManualProcesses)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("INVALID_NAME", "Order type name is required.");

        Name = name.Trim();
        AllowsManualProcesses = allowsManualProcesses;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
