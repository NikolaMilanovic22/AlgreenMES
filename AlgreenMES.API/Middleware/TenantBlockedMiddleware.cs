using System.Text.Json;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Tenancy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;

namespace AlgreenMES.API.Middleware;

/// <summary>
/// Rejects authenticated requests whose tenant is currently inactive
/// (blocked or otherwise IsActive=false). Without this, a JWT issued
/// before the block stays usable until expiry — so a tenant blocked at
/// 10:00 keeps making changes until their token rolls over hours later.
/// For a billing-driven block that's exactly the failure we want to
/// prevent (Saša 18.06.2026).
///
/// SuperAdmins bypass — they own the platform and must be able to
/// reach the unblock endpoint even on a "self-blocked" tenant code.
///
/// Runs AFTER UseAuthentication so HttpContext.User claims are populated
/// and AFTER routing so we can skip non-MVC endpoints (health, SignalR,
/// static files). Login + token-refresh routes are excluded so a user
/// can attempt to re-authenticate (which will then fail with TENANT_BLOCKED
/// via the existing LoginCommandHandler check, returning a friendlier
/// error than a session-killer 403).
/// </summary>
public class TenantBlockedMiddleware
{
    private static readonly HashSet<string> AlwaysAllowedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/refresh",
        "/api/auth/logout",
    };

    private readonly RequestDelegate _next;

    public TenantBlockedMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantService tenantService, TenancyDbContext tenancyDb)
    {
        var matchedMvcAction = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() != null;
        if (!matchedMvcAction) { await _next(context); return; }

        if (context.User?.Identity?.IsAuthenticated != true) { await _next(context); return; }
        if (context.User.IsInRole("SuperAdmin")) { await _next(context); return; }

        var path = context.Request.Path.Value ?? string.Empty;
        if (AlwaysAllowedPaths.Contains(path)) { await _next(context); return; }

        Guid tenantId;
        try { tenantId = tenantService.GetCurrentTenantId(); }
        catch { await _next(context); return; }

        if (tenantId == Guid.Empty) { await _next(context); return; }

        var isActive = await tenancyDb.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => (bool?)t.IsActive)
            .FirstOrDefaultAsync(context.RequestAborted);

        // Unknown tenant → let downstream handlers deal with it.
        if (isActive is null or true) { await _next(context); return; }

        // Tenant is inactive (block or legacy deactivation). Match the
        // login handler's error code so the FE can use the same i18n key
        // and surface a clean message before forcing logout.
        var blockedAt = await tenancyDb.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.BlockedAt)
            .FirstOrDefaultAsync(context.RequestAborted);

        var code = blockedAt.HasValue ? "TENANT_BLOCKED" : "TENANT_INACTIVE";
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        var payload = new
        {
            error = new { code, message = "Tenant access has been revoked." }
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
