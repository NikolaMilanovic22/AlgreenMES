using AlGreenMES.Modules.Identity.Domain.Entities;

namespace AlGreenMES.Modules.Identity.Api.Requests;

public record CreateUserRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    UserRole Role,
    List<Guid>? ProcessIds,
    /// <summary>
    /// Override the tenant this user is created in. SuperAdmin only —
    /// the controller rejects this for non-SuperAdmin callers. Used by
    /// the tenant-creation flow to seed the initial Admin in the
    /// freshly-created tenant (Milos 15.06.2026).
    /// </summary>
    Guid? TenantId = null);
