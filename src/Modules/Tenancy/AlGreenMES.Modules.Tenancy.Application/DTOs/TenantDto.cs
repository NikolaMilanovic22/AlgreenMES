namespace AlGreenMES.Modules.Tenancy.Application.DTOs;

public record TenantDto(
    Guid Id,
    string Name,
    string Code,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? LogoUrl = null,
    DateTime? BlockedAt = null,
    string? BlockedReason = null,
    DateTime? LastPaidAt = null,
    /// <summary>
    /// Latest payment.PeriodEnd for this tenant — the date through which
    /// the subscription is considered paid. Null when no payments have
    /// been recorded. FE compares against today to flag "Kasni" (overdue).
    /// </summary>
    DateTime? PaidThrough = null,
    /// <summary>
    /// Feature keys the SA has disabled for this tenant. FE hides menu
    /// items + routes whose featureKey appears in this list. Empty means
    /// everything enabled.
    /// </summary>
    List<string>? DisabledFeatures = null);
