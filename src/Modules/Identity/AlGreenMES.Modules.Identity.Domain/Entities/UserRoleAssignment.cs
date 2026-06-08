namespace AlGreenMES.Modules.Identity.Domain.Entities;

/// <summary>
/// Extra roles a user has IN ADDITION to their primary <see cref="User.Role"/>.
/// Saša 08.06.2026: introduced for the Magacin module — a user can be e.g.
/// Coordinator (primary) and also Magacioner (additional). Effective role set
/// at auth time = {primary} ∪ {additional}. Every effective role gets emitted
/// as a separate JWT Role claim so [Authorize(Roles = "...")] works without
/// further changes.
/// </summary>
public class UserRoleAssignment
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public UserRole Role { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public User User { get; private set; } = null!;

    private UserRoleAssignment() { }

    internal static UserRoleAssignment Create(Guid tenantId, Guid userId, UserRole role)
    {
        return new UserRoleAssignment
        {
            TenantId = tenantId,
            UserId = userId,
            Role = role
        };
    }
}
