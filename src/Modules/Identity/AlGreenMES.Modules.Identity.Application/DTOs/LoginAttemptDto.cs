namespace AlGreenMES.Modules.Identity.Application.DTOs;

/// <summary>
/// One row of login-attempt history rendered in the user-detail drawer.
/// Returned newest-first. <see cref="FailureReason"/> is null when
/// <see cref="Succeeded"/> is true.
/// </summary>
public record LoginAttemptDto(
    Guid Id,
    string Email,
    string? IpAddress,
    string? UserAgent,
    bool Succeeded,
    string? FailureReason,
    DateTime AttemptedAt);
