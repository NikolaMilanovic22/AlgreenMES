using System.Text.Json;

namespace AlgreenMES.API.Middleware;

/// <summary>
/// Blocks all non-GET requests when the JWT carries
/// <c>cross_tenant_session=true</c>. The claim is set when a SuperAdmin
/// logs into a tenant that isn't their home tenant — they're allowed to
/// browse the foreign tenant for support / debug, but can't accidentally
/// trigger a workflow / save / delete in production.
///
/// One middleware covers every existing and future write endpoint — there's
/// no per-form / per-controller wiring to maintain.
///
/// Runs AFTER UseAuthentication so HttpContext.User.Claims is populated.
/// </summary>
public class CrossTenantReadOnlyMiddleware
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Get,
        HttpMethods.Head,
        HttpMethods.Options,
    };

    private readonly RequestDelegate _next;

    public CrossTenantReadOnlyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated == true
            && string.Equals(context.User.FindFirst("cross_tenant_session")?.Value, "true", StringComparison.OrdinalIgnoreCase)
            && !SafeMethods.Contains(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            var payload = new
            {
                error = new
                {
                    code = "READ_ONLY_CROSS_TENANT",
                    message = "Cross-tenant SuperAdmin session is read-only and cannot modify another tenant's data.",
                }
            };
            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
            return;
        }

        await _next(context);
    }
}
