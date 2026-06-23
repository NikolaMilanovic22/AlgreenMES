using AlGreenMES.Modules.Orders.Domain.Enums;

namespace AlGreenMES.Modules.Orders.Application.DTOs;

public record OrderDto(
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
    int ItemCount,
    DateTime? CompletedAt,
    bool IsInvoiced);
