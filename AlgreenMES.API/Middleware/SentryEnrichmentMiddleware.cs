using AlGreenMES.BuildingBlocks.Common.Interfaces;
using Sentry;

namespace AlgreenMES.API.Middleware;

/// <summary>
/// Tags Sentry events with tenant_id and user_id resolved from the current JWT.
/// Sits in the pipeline after authentication so the JWT claims are available.
/// Anonymous endpoints: tags are skipped (no user, no tenant), event still flows.
/// </summary>
public class SentryEnrichmentMiddleware
{
    private readonly RequestDelegate _next;

    public SentryEnrichmentMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        ITenantService tenantService,
        ICurrentUserService currentUserService,
        IHub sentryHub)
    {
        if (sentryHub.IsEnabled && context.User.Identity?.IsAuthenticated == true)
        {
            sentryHub.ConfigureScope(scope =>
            {
                try
                {
                    var tenantId = tenantService.GetCurrentTenantId();
                    if (tenantId != Guid.Empty)
                        scope.SetTag("tenant_id", tenantId.ToString());
                }
                catch { /* tenant not resolvable */ }

                try
                {
                    var userId = currentUserService.GetCurrentUserId();
                    scope.User = new SentryUser { Id = userId.ToString() };
                }
                catch { /* user not resolvable */ }
            });
        }

        await _next(context);
    }
}
