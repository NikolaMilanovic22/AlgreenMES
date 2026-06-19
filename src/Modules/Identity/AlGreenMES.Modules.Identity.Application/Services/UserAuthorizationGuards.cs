using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Identity.Domain.Entities;

namespace AlGreenMES.Modules.Identity.Application.Services;

/// <summary>
/// Shared authorization checks for user-management handlers (Update /
/// Delete / ResetPassword). Extracted so the cross-tenant boundary and
/// SA-exemption rules stay identical across every mutation entry point —
/// each handler used to inline the same three-clause check, which is the
/// kind of duplication that drifts under pressure.
/// </summary>
public static class UserAuthorizationGuards
{
    /// <summary>
    /// Enforces the cross-tenant boundary on a user-management mutation.
    /// A SuperAdmin caller is exempt (they operate across tenants). A
    /// SuperAdmin target is exempt because they're tenantless — the
    /// role-based peer-SA guard in the handler returns 403 for them
    /// instead. For every other case, a non-matching tenant returns 404
    /// (NotFoundException) so cross-tenant existence isn't leaked.
    /// </summary>
    public static void RequireSameTenantOrSuperAdminTarget(
        ICurrentUserService currentUser,
        User target)
    {
        if (currentUser.IsSuperAdmin) return;
        if (target.Role == UserRole.SuperAdmin) return;
        if (target.TenantId != currentUser.GetCurrentTenantId())
            throw new NotFoundException("User", target.Id);
    }
}
