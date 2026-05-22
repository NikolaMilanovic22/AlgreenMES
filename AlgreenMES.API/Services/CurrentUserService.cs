using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AlGreenMES.BuildingBlocks.Common.Interfaces;

namespace AlgreenMES.API.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role) =>
        _httpContextAccessor.HttpContext?.User?.IsInRole(role) ?? false;

    public Guid GetCurrentTenantId()
    {
        // Background services and pre-auth flows have no HTTP context. Returning
        // Guid.Empty here means EF's HasQueryFilter (Sprint 2.4a) compiles to
        // `WHERE TenantId == Guid.Empty`, which matches no real rows — a safe
        // default that fails closed without breaking startup or background work.
        // Code paths that legitimately need to bypass the filter use
        // .IgnoreQueryFilters() on the query and pass tenantId explicitly.
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null) return Guid.Empty;

        var claim = ctx.User?.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(claim) || !Guid.TryParse(claim, out var tenantId))
        {
            return Guid.Empty;
        }
        return tenantId;
    }

    public Guid GetCurrentUserId()
    {
        var ctx = _httpContextAccessor.HttpContext
            ?? throw new UnauthorizedAccessException("No HTTP context — cannot resolve user.");

        var claim = ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? ctx.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (string.IsNullOrEmpty(claim) || !Guid.TryParse(claim, out var userId))
        {
            throw new UnauthorizedAccessException("Missing or invalid user id claim.");
        }
        return userId;
    }
}
