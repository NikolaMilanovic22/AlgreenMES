namespace AlGreenMES.Modules.Orders.Application.DTOs;

/// <summary>
/// Active session for the calling worker plus pre-computed timestamps the
/// tablet uses to show the auto-logout countdown banner (Bojan spec
/// 25.05.2026, lazy approach 26.05.2026 — no server-pushed alarm).
///
/// alarmAtUtc fires when now() ≥ alarmAtUtc; logoutAtUtc is when reports
/// will treat the session as auto-closed. Both null if no shift matches
/// the check-in time (worker checked in outside any configured shift).
/// </summary>
public record ActiveWorkSessionDto(
    WorkSessionDto Session,
    DateTime? AlarmAtUtc,
    DateTime? LogoutAtUtc);
