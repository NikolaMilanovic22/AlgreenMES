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
    // OrderType is a free-form per-tenant code string (Saša 20.06.2026
    // refactor; admins can create custom codes beyond the original 4).
    // Was the C# OrderType enum here, which made Mapster silently coerce
    // unknown codes to Standard and broke activate-then-refetch on
    // orders with custom types — Saša bug 23.06.2026.
    string OrderType,
    OrderStatus Status,
    string? Notes,
    int? CustomWarningDays,
    int? CustomCriticalDays,
    List<OrderItemDto> Items,
    List<OrderAttachmentDto> Attachments,
    List<OrderManualProcessDto> ManualProcesses,
    List<OrderManualDependencyDto> ManualProcessDependencies);
