namespace AlGreenMES.Modules.Tenancy.Application.DTOs;

/// <summary>
/// Aggregated payment row used by the SA "Sve uplate" cross-tenant view.
/// Carries the tenant name + code denormalised so the FE renders the row
/// without resolving a separate tenant fetch per payment.
/// </summary>
public record AllTenantPaymentDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string TenantCode,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    decimal Amount,
    string Currency,
    DateTime PaidAt,
    string? InvoiceNumber,
    string? Notes,
    DateTime CreatedAt);
