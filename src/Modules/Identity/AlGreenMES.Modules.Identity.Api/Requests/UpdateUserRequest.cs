using AlGreenMES.Modules.Identity.Domain.Entities;

namespace AlGreenMES.Modules.Identity.Api.Requests;

public record UpdateUserRequest(
    string FirstName,
    string LastName,
    UserRole Role,
    bool IsActive,
    bool CanIncludeWithdrawnInAnalysis,
    List<Guid>? ProcessIds,
    /// <summary>Null = leave existing additional roles alone; non-null
    /// = replace them. Saša 08.06.2026 multi-role.</summary>
    List<UserRole>? AdditionalRoles);
