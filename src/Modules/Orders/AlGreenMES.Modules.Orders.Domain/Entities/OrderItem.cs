using AlGreenMES.BuildingBlocks.Common.Entities;
using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Production.Domain.Enums;

namespace AlGreenMES.Modules.Orders.Domain.Entities;

public class OrderItem : AuditableEntity
{
    public Guid OrderId { get; private set; }
    public Guid ProductCategoryId { get; private set; }
    public string ProductName { get; private set; } = null!;
    public int Quantity { get; private set; }
    public string? Notes { get; private set; }

    public Order Order { get; private set; } = null!;

    private readonly List<OrderItemProcess> _processes = new();
    public IReadOnlyCollection<OrderItemProcess> Processes => _processes.AsReadOnly();

    private readonly List<OrderItemSpecialRequest> _specialRequests = new();
    public IReadOnlyCollection<OrderItemSpecialRequest> SpecialRequests => _specialRequests.AsReadOnly();

    private OrderItem()
    {
    }

    internal static OrderItem Create(Guid tenantId, Guid orderId, Guid productCategoryId,
        string? productName, int quantity, string? notes)
    {
        return new OrderItem
        {
            TenantId = tenantId,
            OrderId = orderId,
            ProductCategoryId = productCategoryId,
            ProductName = productName?.Trim() ?? string.Empty,
            Quantity = quantity,
            Notes = notes?.Trim()
        };
    }

    public void Update(string? productName, int quantity, string? notes)
    {
        if (quantity <= 0)
            throw new DomainException("INVALID_QUANTITY", "Quantity must be positive.");

        ProductName = productName?.Trim() ?? string.Empty;
        Quantity = quantity;
        Notes = notes?.Trim();
    }

    public void AddSpecialRequest(Guid specialRequestTypeId)
    {
        if (_specialRequests.Any(sr => sr.SpecialRequestTypeId == specialRequestTypeId))
            throw new DomainException("DUPLICATE_REQUEST", "Special request already added.");

        _specialRequests.Add(new OrderItemSpecialRequest(TenantIdRequired, Id, specialRequestTypeId));
    }

    public void RemoveSpecialRequest(Guid specialRequestTypeId)
    {
        var sr = _specialRequests.FirstOrDefault(s => s.SpecialRequestTypeId == specialRequestTypeId)
            ?? throw new NotFoundException("SpecialRequest", specialRequestTypeId);
        _specialRequests.Remove(sr);
    }

    public OrderItemProcess AddProcess(Guid processId, ComplexityType? complexity, bool overridden = false)
    {
        if (_processes.Any(p => p.ProcessId == processId))
            throw new DomainException("DUPLICATE_PROCESS", "Process already added.");

        var process = OrderItemProcess.Create(TenantIdRequired, Id, processId, complexity, overridden);
        _processes.Add(process);
        return process;
    }

    public bool IsCompleted()
    {
        return _processes.All(p => p.Status == Enums.ProcessStatus.Completed || p.Status == Enums.ProcessStatus.Withdrawn);
    }
}
