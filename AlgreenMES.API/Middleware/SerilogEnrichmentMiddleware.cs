using AlGreenMES.BuildingBlocks.Common.Interfaces;
using Serilog.Context;

namespace AlgreenMES.API.Middleware;

public class SerilogEnrichmentMiddleware
{
    private readonly RequestDelegate _next;

    public SerilogEnrichmentMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        ITenantService tenantService,
        ICurrentUserService currentUserService)
    {
        var requestId = context.TraceIdentifier;
        var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                            ?? Guid.NewGuid().ToString();

        Guid? tenantId = null;
        Guid? userId = null;

        if (context.User.Identity?.IsAuthenticated == true)
        {
            try { tenantId = tenantService.GetCurrentTenantId(); }
            catch { /* tenant not resolvable */ }

            try { userId = currentUserService.GetCurrentUserId(); }
            catch { /* user not resolvable */ }
        }

        using (LogContext.PushProperty("RequestId", requestId))
        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("TenantId", tenantId))
        using (LogContext.PushProperty("UserId", userId))
        {
            context.Response.Headers["X-Correlation-Id"] = correlationId;
            await _next(context);
        }
    }
}
