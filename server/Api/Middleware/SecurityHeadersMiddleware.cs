namespace Api.Middleware;

/// <summary>
/// Adds baseline browser security headers on every response (ZAP / browser hardening).
/// Headers are applied before the rest of the pipeline so auth Set-Cookie is unaffected.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        // CSP present for scanners without breaking OIDC cross-host login on PR previews.
        if (!headers.ContainsKey("Content-Security-Policy"))
        {
            headers["Content-Security-Policy"] =
                "default-src 'self' https: data: blob: 'unsafe-inline' 'unsafe-eval'; " +
                "frame-ancestors 'none'; " +
                "base-uri 'self'; " +
                "object-src 'none'";
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            && !headers.ContainsKey("Cache-Control"))
        {
            headers["Cache-Control"] = "no-store";
        }

        await _next(context);
    }
}
