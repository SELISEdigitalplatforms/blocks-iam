namespace Api.Middleware;

/// <summary>
/// Browser security headers via Response.OnStarting so Genesis/OIDC Set-Cookie
/// still works on PR previews. Skips Cache-Control on cookie-issuing auth paths.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    // Built once from configuration at startup -- the policy does not vary per request.
    private readonly string _contentSecurityPolicy;

    public SecurityHeadersMiddleware(RequestDelegate next, string contentSecurityPolicy)
    {
        _next = next;
        _contentSecurityPolicy = contentSecurityPolicy;
    }

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            Apply(context);
            return Task.CompletedTask;
        });

        return _next(context);
    }

    private void Apply(HttpContext context)
    {
        var headers = context.Response.Headers;
        var path = context.Request.Path.Value ?? string.Empty;
        var isOidcOrTokenPath = IsOidcOrTokenPath(path);

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        // Soft referrer: no-referrer on the SPA breaks /api/oidc/token on previews.
        // Join page (A1): no-referrer so the code fragment cannot leak via Referer.
        headers["Referrer-Policy"] = path.StartsWith("/oidc/join", StringComparison.OrdinalIgnoreCase)
            ? "no-referrer"
            : "strict-origin-when-cross-origin";

        if (!headers.ContainsKey("Content-Security-Policy"))
        {
            headers["Content-Security-Policy"] = _contentSecurityPolicy;
        }

        if (!isOidcOrTokenPath && !headers.ContainsKey("Cache-Control"))
        {
            ApplyCacheControl(headers, path);
        }

        if ((path.StartsWith("/api/oidc/authorize", StringComparison.OrdinalIgnoreCase)
             || path.StartsWith("/oidc/authorize", StringComparison.OrdinalIgnoreCase))
            && !headers.ContainsKey("Content-Type")
            && context.Response.StatusCode < 300)
        {
            headers["Content-Type"] = "text/html; charset=utf-8";
        }
    }

    private static bool IsOidcOrTokenPath(string path) =>
        path.StartsWith("/api/oidc", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/oidc", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/api/idp", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/api/auth/token", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/api/auth/refresh", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/connect/token", StringComparison.OrdinalIgnoreCase);

    private static void ApplyCacheControl(IHeaderDictionary headers, string path)
    {
        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            || path == "/"
            || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("runtime-config.js", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("theme-init.js", StringComparison.OrdinalIgnoreCase)
            || !Path.HasExtension(path))
        {
            headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            headers["Pragma"] = "no-cache";
        }
        else if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
        {
            headers["Cache-Control"] = "public, max-age=31536000, immutable";
        }
    }
}
