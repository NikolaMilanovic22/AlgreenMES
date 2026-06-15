using AlGreenMES.Modules.Identity.Domain.Entities;

namespace AlGreenMES.Modules.Identity.Application.Services;

public interface IJwtTokenService
{
    /// <summary>Standard token — user logs into their home tenant.</summary>
    string GenerateToken(User user);

    /// <summary>
    /// Cross-tenant token for a SuperAdmin logging into a tenant that
    /// isn't their home. The token's <c>tenant_id</c> claim is the target
    /// tenant (so the API scopes reads to that tenant), and
    /// <c>cross_tenant_session=true</c> is set so the read-only middleware
    /// can block all writes.
    /// </summary>
    string GenerateCrossTenantToken(User superAdminUser, Guid targetTenantId);

    string GenerateRefreshToken();
}
