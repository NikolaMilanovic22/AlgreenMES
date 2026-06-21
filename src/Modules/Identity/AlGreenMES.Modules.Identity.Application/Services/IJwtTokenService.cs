using AlGreenMES.Modules.Identity.Domain.Entities;

namespace AlGreenMES.Modules.Identity.Application.Services;

public interface IJwtTokenService
{
    /// <summary>
    /// Issue an access token for <paramref name="user"/> scoped to
    /// <paramref name="effectiveTenantId"/>. For a normal user that's their
    /// home tenant. For a SuperAdmin (who has no home tenant since
    /// 16.06.2026) it's the tenant code they typed at login — the API
    /// then scopes reads to that tenant. Writes are gated separately by
    /// the SuperAdminReadOnly middleware.
    /// </summary>
    string GenerateToken(User user, Guid effectiveTenantId);

    string GenerateRefreshToken();
}
