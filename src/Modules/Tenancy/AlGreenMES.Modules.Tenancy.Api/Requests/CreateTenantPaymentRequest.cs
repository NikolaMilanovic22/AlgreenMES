namespace AlGreenMES.Modules.Tenancy.Api.Requests;

public record CreateTenantPaymentRequest(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    decimal Amount,
    string Currency,
    DateTime PaidAt,
    string? InvoiceNumber,
    string? Notes);
