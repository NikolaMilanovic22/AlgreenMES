namespace AlGreenMES.Modules.Identity.Domain.Entities;

/// <summary>
/// One row per login attempt — success or failure — for the audit trail.
/// Lets incident response reconstruct "who tried to log in as this user,
/// from where, and when did they succeed?" without having to grep request
/// logs (which roll off after 30 days and don't capture body fields).
///
/// Not tied to a tenant query filter on purpose: a failure with a wrong
/// tenant code can't resolve a tenant, and we still want to log it. The
/// <see cref="TenantId"/> column is nullable for that case.
/// </summary>
public class LoginAttempt
{
    public Guid Id { get; private set; }

    /// <summary>
    /// Tenant the attempted login was scoped to. Null when the tenant code
    /// supplied at login didn't resolve to any tenant (= failed pre-auth).
    /// </summary>
    public Guid? TenantId { get; private set; }

    /// <summary>Email exactly as the client submitted, lower-cased.</summary>
    public string Email { get; private set; } = null!;

    /// <summary>
    /// Originating IP per X-Forwarded-For first hop, falling back to the
    /// socket address. Nullable because some edge cases (server-side test
    /// calls, malformed requests) won't have one.
    /// </summary>
    public string? IpAddress { get; private set; }

    /// <summary>User-Agent header value, truncated to 256 chars.</summary>
    public string? UserAgent { get; private set; }

    public bool Succeeded { get; private set; }

    /// <summary>
    /// On failure, the error code thrown by the login handler
    /// (INVALID_CREDENTIALS, USER_INACTIVE, TENANT_INACTIVE,
    /// ACCOUNT_LOCKED, TENANT_NOT_FOUND). Null on success.
    /// </summary>
    public string? FailureReason { get; private set; }

    public DateTime AttemptedAt { get; private set; }

    private LoginAttempt() { }

    public static LoginAttempt RecordSuccess(Guid tenantId, string email, string? ipAddress, string? userAgent, DateTime nowUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email,
            IpAddress = ipAddress,
            UserAgent = Truncate(userAgent, 256),
            Succeeded = true,
            FailureReason = null,
            AttemptedAt = nowUtc,
        };

    public static LoginAttempt RecordFailure(Guid? tenantId, string email, string failureReason, string? ipAddress, string? userAgent, DateTime nowUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email,
            IpAddress = ipAddress,
            UserAgent = Truncate(userAgent, 256),
            Succeeded = false,
            FailureReason = failureReason,
            AttemptedAt = nowUtc,
        };

    private static string? Truncate(string? value, int maxLength)
    {
        if (value == null) return null;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
