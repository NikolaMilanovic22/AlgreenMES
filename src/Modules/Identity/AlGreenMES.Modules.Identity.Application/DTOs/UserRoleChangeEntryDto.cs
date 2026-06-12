namespace AlGreenMES.Modules.Identity.Application.DTOs;

/// <summary>
/// One row of role-change history rendered in the user-detail drawer.
/// The actor's name is resolved server-side because the FE can't always
/// look it up — a long-departed admin may still appear here even after
/// being de-activated, and falling back to the raw GUID is bad UX.
/// </summary>
public record UserRoleChangeEntryDto(
    Guid Id,
    string OldRole,
    string NewRole,
    Guid ChangedByUserId,
    string? ChangedByUserName,
    DateTime ChangedAt,
    string? Reason);
