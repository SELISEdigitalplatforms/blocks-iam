namespace Api.Middleware;

/// <summary>
/// Baseline security headers. Applied before the rest of the pipeline (no OnStarting)
/// and without Cache-Control, so OIDC Set-Cookie on PR previews keeps working.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        if (!headers.ContainsKey("Content-Security-Policy"))
        {
            headers["Content-Security-Policy"] =
                "default-src 'self' https: data: blob: 'unsafe-inline' 'unsafe-eval'; " +
                "frame-ancestors 'none'; base-uri 'self'; object-src 'none'";
        }

        return _next(context);
    }
}
