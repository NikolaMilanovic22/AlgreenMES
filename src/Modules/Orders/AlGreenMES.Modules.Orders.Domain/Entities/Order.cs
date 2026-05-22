using AlGreenMES.BuildingBlocks.Common.Entities;
using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Production.Domain.Enums;

namespace AlGreenMES.Modules.Orders.Domain.Entities;

public class Order : AuditableEntity
{
    public string OrderNumber { get; private set; } = null!;
    public DateTime DeliveryDate { get; private set; }
    public int Priority { get; private set; }
    public OrderType OrderType { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public int? CustomWarningDays { get; private set; }
    public int? CustomCriticalDays { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public bool IsInvoiced { get; private set; }

    private readonly List<OrderItem> _items = new();
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    private readonly List<OrderAttachment> _attachments = new();
    public IReadOnlyCollection<OrderAttachment> Attachments => _attachments.AsReadOnly();

    // Manual processes — used when the order's OrderType has AllowsManualProcesses=true.
    // Their presence overrides the per-item product-category processes at item-creation
    // time (CreateOrderCommandHandler).
    private readonly List<OrderManualProcess> _manualProcesses = new();
    public IReadOnlyCollection<OrderManualProcess> ManualProcesses => _manualProcesses.AsReadOnly();

    private readonly List<OrderManualProcessDependency> _manualProcessDependencies = new();
    public IReadOnlyCollection<OrderManualProcessDependency> ManualProcessDependencies => _manualProcessDependencies.AsReadOnly();

    public bool HasManualProcesses => _manualProcesses.Count > 0;

    private Order()
    {
    }

    public static Order Create(Guid tenantId, string orderNumber, DateTime deliveryDate,
        int priority, OrderType orderType, Guid createdByUserId, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            throw new DomainException("INVALID_ORDER_NUMBER", "Order number is required.");
        if (deliveryDate.Date <= DateTime.UtcNow.Date)
            throw new DomainException("INVALID_DATE", "Delivery date must be in the future.");
        if (priority <= 0)
            throw new DomainException("INVALID_PRIORITY", "Priority must be positive.");

        var order = new Order
        {
            TenantId = tenantId,
            OrderNumber = orderNumber.Trim(),
            DeliveryDate = deliveryDate,
            Priority = priority,
            OrderType = orderType,
            Status = OrderStatus.Draft,
            Notes = notes?.Trim()
        };
        order.SetCreated(createdByUserId);
        return order;
    }

    public OrderItem AddItem(Guid productCategoryId, string? productName, int quantity, string? notes = null)
    {
        if (Status != OrderStatus.Draft)
            throw new DomainException("ORDER_NOT_DRAFT", "Can only add items to draft orders.");
        if (quantity <= 0)
            throw new DomainException("INVALID_QUANTITY", "Quantity must be positive.");

        var item = OrderItem.Create(TenantId, Id, productCategoryId, productName, quantity, notes);
        _items.Add(item);
        return item;
    }

    public void RemoveItem(Guid itemId)
    {
        if (Status != OrderStatus.Draft)
            throw new DomainException("ORDER_NOT_DRAFT", "Can only remove items from draft orders.");

        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new NotFoundException("OrderItem", itemId);
        _items.Remove(item);
    }

    public void AddManualProcess(Guid processId, int sequenceOrder, ComplexityType? defaultComplexity)
    {
        if (Status != OrderStatus.Draft)
            throw new DomainException("ORDER_NOT_DRAFT", "Manual processes can only be configured on draft orders.");
        if (_manualProcesses.Any(p => p.ProcessId == processId))
            throw new DomainException("DUPLICATE_PROCESS", "This process is already added.");

        _manualProcesses.Add(OrderManualProcess.Create(TenantId, Id, processId, sequenceOrder, defaultComplexity));
    }

    public void AddManualDependency(Guid processId, Guid dependsOnProcessId)
    {
        if (Status != OrderStatus.Draft)
            throw new DomainException("ORDER_NOT_DRAFT", "Manual dependencies can only be configured on draft orders.");
        if (!_manualProcesses.Any(p => p.ProcessId == processId))
            throw new DomainException("PROCESS_NOT_IN_ORDER", "Process is not part of the order's manual processes.");
        if (!_manualProcesses.Any(p => p.ProcessId == dependsOnProcessId))
            throw new DomainException("PROCESS_NOT_IN_ORDER", "Dependency target is not part of the order's manual processes.");
        if (_manualProcessDependencies.Any(d => d.ProcessId == processId && d.DependsOnProcessId == dependsOnProcessId))
            throw new DomainException("DUPLICATE_DEPENDENCY", "This dependency already exists.");
        if (WouldCreateCycle(processId, dependsOnProcessId))
            throw new DomainException("CIRCULAR_DEPENDENCY", "This would create a circular dependency.");

        _manualProcessDependencies.Add(OrderManualProcessDependency.Create(TenantId, Id, processId, dependsOnProcessId));
    }

    /// <summary>BFS forward through existing edges from <paramref name="dependsOnProcessId"/> —
    /// if we can reach <paramref name="processId"/>, adding the new edge would close the loop.</summary>
    private bool WouldCreateCycle(Guid processId, Guid dependsOnProcessId)
    {
        var visited = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(dependsOnProcessId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == processId) return true;
            if (!visited.Add(current)) continue;

            foreach (var dep in _manualProcessDependencies.Where(d => d.ProcessId == current))
                queue.Enqueue(dep.DependsOnProcessId);
        }
        return false;
    }

    public void ClearManualProcesses()
    {
        if (Status != OrderStatus.Draft)
            throw new DomainException("ORDER_NOT_DRAFT", "Manual processes can only be modified on draft orders.");
        _manualProcessDependencies.Clear();
        _manualProcesses.Clear();
    }

    public void Activate()
    {
        if (Status != OrderStatus.Draft)
            throw new DomainException("INVALID_STATUS", "Only draft orders can be activated.");
        if (!_items.Any())
            throw new DomainException("NO_ITEMS", "Order must have at least one item.");
        if (Priority <= 0)
            throw new DomainException("PRIORITY_REQUIRED", "Priority must be set before activation.");

        Status = OrderStatus.Active;
    }

    public void Pause()
    {
        if (Status != OrderStatus.Active)
            throw new DomainException("INVALID_STATUS", "Only active orders can be paused.");
        Status = OrderStatus.Paused;
    }

    public void Resume()
    {
        if (Status != OrderStatus.Paused)
            throw new DomainException("INVALID_STATUS", "Only paused orders can be resumed.");
        Status = OrderStatus.Active;
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Completed || Status == OrderStatus.Cancelled)
            throw new DomainException("INVALID_STATUS", "Cannot cancel completed or already cancelled orders.");

        // Stop all running process timers (both main and sub-process timers)
        foreach (var item in _items)
        {
            foreach (var proc in item.Processes)
            {
                if (proc.Status != Enums.ProcessStatus.InProgress) continue;

                // Close any open sub-process logs first
                foreach (var sub in proc.SubProcesses)
                {
                    if (sub.Status == Enums.SubProcessStatus.InProgress)
                    {
                        var openLog = sub.GetOpenLog();
                        if (openLog != null)
                        {
                            openLog.End();
                            if (openLog.DurationMinutes.HasValue)
                                sub.AddDuration(openLog.DurationMinutes.Value);
                        }
                    }
                }

                // Pause main process timer (for no-sub-process path)
                if (!proc.PausedAt.HasValue)
                {
                    proc.Pause();
                }
            }
        }

        Status = OrderStatus.Cancelled;
        Priority = 0;
    }

    public void Reopen()
    {
        if (Status != OrderStatus.Cancelled)
            throw new DomainException("INVALID_STATUS", "Only cancelled orders can be reopened.");
        Status = OrderStatus.Draft;
    }

    public void ChangePriority(int newPriority)
    {
        if (newPriority <= 0)
            throw new DomainException("INVALID_PRIORITY", "Priority must be positive.");
        if (HasProductionStarted())
            throw new DomainException("PRODUCTION_STARTED", "Cannot change priority after production started.");
        Priority = newPriority;
    }

    public void UndoComplete()
    {
        if (Status != OrderStatus.Completed)
            throw new DomainException("INVALID_STATUS", "Only completed orders can be reverted.");
        Status = OrderStatus.Active;
        CompletedAt = null;
    }

    public bool HasProductionStarted()
    {
        return _items.Any(i => i.Processes.Any(p => p.Status != Enums.ProcessStatus.Pending));
    }

    public void SetCustomWarningDays(int? warningDays, int? criticalDays)
    {
        CustomWarningDays = warningDays;
        CustomCriticalDays = criticalDays;
    }

    public void MarkCompleted()
    {
        if (_items.All(i => i.IsCompleted()))
        {
            Status = OrderStatus.Completed;
            Priority = 0;
            CompletedAt = DateTime.UtcNow;
        }
    }

    public void SetInvoiced(bool invoiced)
    {
        IsInvoiced = invoiced;
    }

    public void Update(string? orderNumber, DateTime? deliveryDate, string? notes, int? customWarningDays, int? customCriticalDays)
    {
        if (orderNumber != null)
        {
            if (string.IsNullOrWhiteSpace(orderNumber))
                throw new DomainException("INVALID_ORDER_NUMBER", "Order number is required.");
            OrderNumber = orderNumber.Trim();
        }
        if (deliveryDate.HasValue)
        {
            DeliveryDate = deliveryDate.Value.Kind == DateTimeKind.Utc
                ? deliveryDate.Value
                : DateTime.SpecifyKind(deliveryDate.Value, DateTimeKind.Utc);
        }
        Notes = notes?.Trim();
        CustomWarningDays = customWarningDays;
        CustomCriticalDays = customCriticalDays;
    }
}
