using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Production.Domain.Enums;

namespace AlGreenMES.Modules.Orders.Application.DTOs;

public record OrderManualProcessDto(Guid ProcessId, int SequenceOrder, ComplexityType? DefaultComplexity);
public record OrderManualDependencyDto(Guid ProcessId, Guid DependsOnProcessId);

public record OrderDetailDto(
    Guid Id,
    Guid TenantId,
    string OrderNumber,
    DateTime DeliveryDate,
    int Priority,
    OrderType OrderType,
    OrderStatus Status,
    string? Notes,
    int? CustomWarningDays,
    int? CustomCriticalDays,
    List<OrderItemDto> Items,
    List<OrderAttachmentDto> Attachments,
    List<OrderManualProcessDto> ManualProcesses,
    List<OrderManualDependencyDto> ManualProcessDependencies);
