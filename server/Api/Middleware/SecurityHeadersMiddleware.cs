using Api.Security;

namespace Api.Middleware;

/// <summary>
/// Browser security headers via Response.OnStarting so Genesis/OIDC Set-Cookie
/// still works on PR previews. Skips Cache-Control on cookie-issuing auth paths.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary><see cref="HttpContext.Items"/> key for the response's style nonce.</summary>
    public const string StyleNonceItemKey = "csp-style-nonce";

    // Built once from configuration at startup; only the style nonce varies per response.
    private readonly string _contentSecurityPolicy;
    private readonly StyleNonce _styleNonces;

    public SecurityHeadersMiddleware(RequestDelegate next, string contentSecurityPolicy, StyleNonce styleNonces)
    {
        _next = next;
        _contentSecurityPolicy = contentSecurityPolicy;
        _styleNonces = styleNonces;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // The response's style nonce, stable per browser (see StyleNonce). The SPA shell is
        // rendered with the same value as the header.
        var styleNonce = _styleNonces.ForRequest(context);
        context.Items[StyleNonceItemKey] = styleNonce;

        context.Response.OnStarting(() =>
        {
            Apply(context, styleNonce);
            return Task.CompletedTask;
        });

        return _next(context);
    }

    private void Apply(HttpContext context, string styleNonce)
    {
        var headers = context.Response.Headers;
        var path = context.Request.Path.Value ?? string.Empty;
        var isOidcOrTokenPath = IsOidcOrTokenPath(path);

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        // Soft referrer: no-referrer on the SPA breaks /api/oidc/token on previews.
        // Invitation page (A1): no-referrer so the code fragment cannot leak via Referer.
        headers["Referrer-Policy"] = path.StartsWith("/oidc/invitation", StringComparison.OrdinalIgnoreCase)
            ? "no-referrer"
            : "strict-origin-when-cross-origin";

        if (!headers.ContainsKey("Content-Security-Policy"))
        {
            headers["Content-Security-Policy"] = ContentSecurityPolicy.WithStyleNonce(_contentSecurityPolicy, styleNonce);
        }

        if (!isOidcOrTokenPath && !headers.ContainsKey(CacheControlHeader))
        {
            ApplyCacheControl(headers, path);
        }
        else if (IsOidcPage(context) && !headers.ContainsKey(CacheControlHeader))
        {
            // The hosted login, error and invitation pages are HTML and must not be cached either.
            // Only GET pages under /oidc: the token, authorize and callback responses that issue
            // cookies keep their own headers.
            DisableCaching(headers);
        }

        if ((path.StartsWith("/api/oidc/authorize", StringComparison.OrdinalIgnoreCase)
             || path.StartsWith("/oidc/authorize", StringComparison.OrdinalIgnoreCase))
            && !headers.ContainsKey("Content-Type")
            && context.Response.StatusCode < 300)
        {
            headers["Content-Type"] = "text/html; charset=utf-8";
        }
    }

    private const string CacheControlHeader = "Cache-Control";
    private const string NoStore = "no-store, no-cache, must-revalidate, max-age=0";

    /// <summary>
    /// Marks a response as not cacheable. The SPA shell calls this itself so it is never cached,
    /// whatever path the fallback answered (including /api/oidc, /api/idp and /login, where
    /// <see cref="Apply"/> leaves Cache-Control to the auth handlers).
    /// </summary>
    public static void DisableCaching(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        headers[CacheControlHeader] = NoStore;
        headers["Pragma"] = "no-cache";
    }

    private static bool IsOidcPage(HttpContext context) =>
        (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        && context.Request.Path.StartsWithSegments("/oidc", StringComparison.OrdinalIgnoreCase)
        && (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) ?? false);

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
            DisableCaching(headers);
        }
        else if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
        {
            headers[CacheControlHeader] = "public, max-age=31536000, immutable";
        }
    }
}
