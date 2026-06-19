using System.Text.Json;
using AlGreenMES.BuildingBlocks.Common.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace AlgreenMES.API.Middleware;

/// <summary>
/// Blocks all non-GET requests from a SuperAdmin caller unless the matched
/// action carries <see cref="AllowSuperAdminWriteAttribute"/>. SuperAdmins
/// are tenantless platform operators (Milos 16.06.2026) — by default they
/// can READ across the system but can't change anyone's data; the opt-out
/// attribute is reserved for the small set of platform-level write
/// operations they explicitly own (create tenants, create other
/// SuperAdmins, change their own password).
///
/// One middleware covers every existing and future write endpoint — no
/// per-controller wiring needed. The attribute makes the exceptions
/// auditable in code review.
///
/// Runs AFTER UseAuthentication so HttpContext.User claims are populated.
/// Endpoint metadata is populated by routing — UseRouting() must precede.
/// </summary>
public class SuperAdminReadOnlyMiddleware
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Get,
        HttpMethods.Head,
        HttpMethods.Options,
    };

    private readonly RequestDelegate _next;

    public SuperAdminReadOnlyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip the SA-write check when no MVC action matched the request —
        // hand it down to the pipeline so MVC returns the proper 404.
        // Without this guard, an SA hitting a typo'd URL gets a misleading
        // SUPERADMIN_READ_ONLY 403 (Milos 17.06.2026: edit-payment PUT
        // looked like it was forbidden when in reality the BE binary was
        // stale and the route didn't exist yet).
        var matchedMvcAction = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() != null;

        if (matchedMvcAction
            && context.User?.Identity?.IsAuthenticated == true
            && context.User.IsInRole(RoleNames.SuperAdmin)
            && !SafeMethods.Contains(context.Request.Method)
            && !HasAllowAttribute(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            var payload = new
            {
                error = new
                {
                    code = "SUPERADMIN_READ_ONLY",
                    message = "SuperAdmin sessions are read-only; this endpoint is not on the platform-write allowlist.",
                }
            };
            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// True when the matched MVC action (or its controller) carries
    /// <see cref="AllowSuperAdminWriteAttribute"/>. Falls back to false on
    /// non-MVC endpoints (e.g. SignalR, health checks, static files) —
    /// SuperAdmins shouldn't be hitting those with writes anyway.
    /// </summary>
    private static bool HasAllowAttribute(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var actionDescriptor = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (actionDescriptor == null)
            return false;

        return actionDescriptor.MethodInfo.IsDefined(typeof(AllowSuperAdminWriteAttribute), inherit: true)
            || actionDescriptor.ControllerTypeInfo.IsDefined(typeof(AllowSuperAdminWriteAttribute), inherit: true);
    }
}
