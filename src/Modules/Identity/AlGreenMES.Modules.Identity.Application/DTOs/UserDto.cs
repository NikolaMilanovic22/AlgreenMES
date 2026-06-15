using AlGreenMES.Modules.Identity.Domain.Entities;

namespace AlGreenMES.Modules.Identity.Application.DTOs;

public record UserDto(
    Guid Id,
    Guid TenantId,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    UserRole Role,
    /// <summary>Extra roles beyond the primary <see cref="Role"/>. Saša
    /// 08.06.2026 — a user can be e.g. Coordinator (primary) + Magacioner
    /// (additional). FE uses this to gate menu items.</summary>
    List<UserRole> AdditionalRoles,
    bool CanIncludeWithdrawnInAnalysis,
    bool IsActive,
    List<UserProcessDto> Processes,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record UserProcessDto(
    Guid ProcessId);
