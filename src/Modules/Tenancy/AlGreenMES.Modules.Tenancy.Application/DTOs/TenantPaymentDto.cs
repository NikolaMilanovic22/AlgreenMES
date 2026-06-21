namespace AlGreenMES.Modules.Tenancy.Application.DTOs;

public record TenantPaymentDto(
    Guid Id,
    Guid TenantId,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    decimal Amount,
    string Currency,
    DateTime PaidAt,
    string? InvoiceNumber,
    string? Notes,
    DateTime CreatedAt);
