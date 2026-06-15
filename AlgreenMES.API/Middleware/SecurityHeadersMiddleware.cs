namespace AlgreenMES.API.Middleware;

/// <summary>
/// Sets a minimum baseline of security-related HTTP response headers on every
/// API response. These are cheap defense-in-depth — none replace TLS, auth,
/// or input validation; they shrink the attack surface against the common
/// browser-side classes (clickjacking, MIME-sniffing, referer leakage, etc.).
///
/// Notes per header:
/// - <c>X-Content-Type-Options: nosniff</c>: stops browsers second-guessing
///   the Content-Type. Protects against polyglot uploads being interpreted
///   as scripts.
/// - <c>X-Frame-Options: DENY</c>: refuses to be embedded in any iframe.
///   The MES dashboards/tablets are never legitimately framed by a third
///   party, so DENY is the safe default. (Same intent as
///   <c>frame-ancestors 'none'</c> below; both kept for browser coverage.)
/// - <c>Referrer-Policy: strict-origin-when-cross-origin</c>: keeps full
///   URLs (which include order numbers, user IDs) from leaking via Referer
///   when a link is clicked out to an external site.
/// - <c>Strict-Transport-Security</c>: enforces HTTPS for 1 year including
///   subdomains. Only sent for non-localhost so dev over plain HTTP keeps
///   working.
/// - <c>Content-Security-Policy: frame-ancestors 'none'</c>: the modern
///   replacement for X-Frame-Options. We deliberately keep the CSP narrow
///   to frame-ancestors only — the API doesn't serve HTML, and a stricter
///   default-src would risk blocking legitimate API responses or the
///   OpenAPI dev page. The FE bundles serve their own CSP at the nginx
///   layer if/when we add one.
/// - <c>X-Permitted-Cross-Domain-Policies: none</c>: legacy Flash/PDF
///   reader fence — almost-free, no downside.
///
/// Adds nothing the BE-side fetch behaviour relies on; only sets response
/// headers, never reads them. Place after <c>UseHttpsRedirection</c> and
/// before <c>UseAuthentication</c> so 401/403 responses also carry them.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Use indexers so the middleware is idempotent if another stage
        // already set the same header (last-write-wins, no duplicates).
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["X-Permitted-Cross-Domain-Policies"] = "none";
        headers["Content-Security-Policy"] = "frame-ancestors 'none'";

        if (context.Request.IsHttps)
        {
            // 1 year, include subdomains. preload omitted on purpose —
            // opting into the browser preload list is a deployment-level
            // commitment that requires HTTPS on every subdomain we'd ever
            // serve from algreen.rs / duckdns.org / easy-mes domains.
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        }

        await _next(context);
    }
}
