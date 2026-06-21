using AlGreenMES.BuildingBlocks.Common.Exceptions;

namespace AlGreenMES.Modules.Tenancy.Domain.Entities;

/// <summary>
/// One line in the SuperAdmin billing ledger for a tenant. Free-form date
/// range so monthly / quarterly / annual subscriptions all fit without
/// schema changes. Invoiced amounts and currency are stored verbatim;
/// no FX conversion is attempted.
/// </summary>
public class TenantPayment
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public DateTime PeriodStart { get; private set; }
    public DateTime PeriodEnd { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public DateTime PaidAt { get; private set; }
    public string? InvoiceNumber { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private TenantPayment() { }

    public static TenantPayment Create(
        Guid tenantId,
        DateTime periodStart,
        DateTime periodEnd,
        decimal amount,
        string currency,
        DateTime paidAt,
        string? invoiceNumber,
        string? notes)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("PAYMENT_TENANT_REQUIRED", "Tenant id is required.");
        if (periodEnd < periodStart)
            throw new DomainException("PAYMENT_PERIOD_INVALID", "Period end must be on or after period start.");
        if (amount <= 0)
            throw new DomainException("PAYMENT_AMOUNT_INVALID", "Amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("PAYMENT_CURRENCY_REQUIRED", "Currency is required.");
        if (currency.Length > 8)
            throw new DomainException("PAYMENT_CURRENCY_TOO_LONG", "Currency code must be 8 characters or less.");

        return new TenantPayment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PeriodStart = DateTime.SpecifyKind(periodStart.Date, DateTimeKind.Utc),
            PeriodEnd = DateTime.SpecifyKind(periodEnd.Date, DateTimeKind.Utc),
            Amount = amount,
            Currency = currency.Trim().ToUpperInvariant(),
            PaidAt = paidAt.Kind == DateTimeKind.Utc ? paidAt : DateTime.SpecifyKind(paidAt, DateTimeKind.Utc),
            InvoiceNumber = string.IsNullOrWhiteSpace(invoiceNumber) ? null : invoiceNumber.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Update(
        DateTime periodStart,
        DateTime periodEnd,
        decimal amount,
        string currency,
        DateTime paidAt,
        string? invoiceNumber,
        string? notes)
    {
        if (periodEnd < periodStart)
            throw new DomainException("PAYMENT_PERIOD_INVALID", "Period end must be on or after period start.");
        if (amount <= 0)
            throw new DomainException("PAYMENT_AMOUNT_INVALID", "Amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("PAYMENT_CURRENCY_REQUIRED", "Currency is required.");
        if (currency.Length > 8)
            throw new DomainException("PAYMENT_CURRENCY_TOO_LONG", "Currency code must be 8 characters or less.");

        PeriodStart = DateTime.SpecifyKind(periodStart.Date, DateTimeKind.Utc);
        PeriodEnd = DateTime.SpecifyKind(periodEnd.Date, DateTimeKind.Utc);
        Amount = amount;
        Currency = currency.Trim().ToUpperInvariant();
        PaidAt = paidAt.Kind == DateTimeKind.Utc ? paidAt : DateTime.SpecifyKind(paidAt, DateTimeKind.Utc);
        InvoiceNumber = string.IsNullOrWhiteSpace(invoiceNumber) ? null : invoiceNumber.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
