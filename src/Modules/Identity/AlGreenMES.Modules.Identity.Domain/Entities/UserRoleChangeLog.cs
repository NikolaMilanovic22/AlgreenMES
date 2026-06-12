namespace AlGreenMES.Modules.Identity.Domain.Entities;

/// <summary>
/// History row for "user X's primary role changed from A to B, by Y, at Z".
/// The audit-interceptor on the User entity tells you who last touched a
/// row, but not the trajectory of the role field. F-9 in the Sprint 3.0
/// audit asks for a history table so an investigator can answer
/// "who demoted me on April 3rd?" without diffing replayed migrations.
///
/// Only the PRIMARY role change is logged (not AdditionalRoles touches) —
/// the F-9 spec singularised it that way, and the primary role is the
/// one that gates the bulk of authz checks anyway.
///
/// Reason is reserved for a future UI that captures an admin's
/// justification; populates as null today.
/// </summary>
public class UserRoleChangeLog
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>The user whose role changed.</summary>
    public Guid UserId { get; private set; }

    public UserRole OldRole { get; private set; }
    public UserRole NewRole { get; private set; }

    /// <summary>The actor who triggered the change.</summary>
    public Guid ChangedByUserId { get; private set; }

    public DateTime ChangedAt { get; private set; }

    public string? Reason { get; private set; }

    private UserRoleChangeLog() { }

    public static UserRoleChangeLog Create(
        Guid tenantId,
        Guid userId,
        UserRole oldRole,
        UserRole newRole,
        Guid changedByUserId,
        DateTime nowUtc,
        string? reason = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            OldRole = oldRole,
            NewRole = newRole,
            ChangedByUserId = changedByUserId,
            ChangedAt = nowUtc,
            Reason = reason,
        };
}
