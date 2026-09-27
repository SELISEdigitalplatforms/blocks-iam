namespace Api.Middleware;

/// <summary>
/// Adds baseline browser security headers on every response (ZAP / browser hardening).
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var httpContext = (HttpContext)state!;
            var headers = httpContext.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

            // CSP present for ZAP/browser hardening without breaking OIDC cross-host
            // login (preview → shared IdP → callback). Tighten further in a dedicated
            // CSP ticket once nonce/hash script loading is in place.
            if (!headers.ContainsKey("Content-Security-Policy"))
            {
                headers["Content-Security-Policy"] =
                    "default-src 'self' https: data: blob: 'unsafe-inline' 'unsafe-eval'; " +
                    "frame-ancestors 'none'; " +
                    "base-uri 'self'; " +
                    "object-src 'none'";
            }

            var path = httpContext.Request.Path.Value ?? string.Empty;
            if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
                && !headers.ContainsKey("Cache-Control"))
            {
                headers["Cache-Control"] = "no-store";
            }

            return Task.CompletedTask;
        }, context);

        return _next(context);
    }
}
