using Azure.Core;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using System.Collections;
using System.Globalization;
using System.Reflection;

namespace Authentication.DomainService.Utilities
{
    /// <summary>
    /// Resolves application and cookie domain in this order:
    /// 1) BlocksContext.ApplicationDomain (or provided blockContextDomain)
    /// 2) Request Origin/Referer host
    /// 3) Fallback to first configured tenant domain
    /// </summary>
    public static class DomainResolver
    {
        /// <summary>
        /// Determines if the current environment or request is localhost/development.
        /// </summary>
        private static IHttpContextAccessor? _httpContextAccessor;

        public static void Configure(IHttpContextAccessor accessor)
        {
            _httpContextAccessor = accessor;
        }

        public static bool IsLocalhost()
        {
            // Check environment variable first
            var hostEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? string.Empty;
            if (hostEnv.Equals("Development", StringComparison.OrdinalIgnoreCase))
                return true;

            return TryGetRequestOriginUri(out var uri) && IsLoopbackHost(uri.Host);
        }

        public static bool IsCrossOriginHttpFlow()
        {
            // True localhost dev -> always None.
            if (IsLocalhost())
                return true;

            // Plain http origin (e.g. local dev with a hosts-file entry on http) -> None.
            // https origins (production) -> Strict: cookies are first-party and secure.
            if (!TryGetRequestOriginUri(out var uri))
                return false;

            return uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                && uri.Port > 0 && uri.Port <= 65535;
        }

        public static bool IsCurrentRequestSecure()
        {
            // Secure is derived purely from the scheme the caller advertised via Origin/Referer.
            // No fallback to Request.IsHttps - that can lie when an upstream proxy terminates TLS.
            return TryGetRequestOriginUri(out var uri)
                && uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetRequestOriginUri(out Uri uri)
        {
            uri = null!;
            var request = _httpContextAccessor?.HttpContext?.Request;
            var origin = request?.Headers.Origin.ToString();
            var referer = request?.Headers.Referer.ToString();

            var raw = !string.IsNullOrWhiteSpace(origin) ? origin : referer;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            return Uri.TryCreate(raw, UriKind.Absolute, out uri);
        }

        private static bool IsLoopbackHost(string host) =>
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || host.Equals("::1", StringComparison.OrdinalIgnoreCase);

        public static (string? domain, string? cookieDomain, bool isResolved) ResolveDomain(
            Tenant? tenant,
            HttpRequest? request)
        {
            var domains = GetTenantDomains(tenant);
            if (domains.Count == 0)
            {
                return (null, null, false);
            }

            var effectiveContextDomain = BlocksContext.ResolveApplicationDomain(request);
            if (!string.IsNullOrWhiteSpace(effectiveContextDomain))
            {
                var matched = FindDomainMatch(domains, effectiveContextDomain);
                if (matched != null)
                {
                    return (matched.Value.domain, matched.Value.cookieDomain, true);
                }
            }

            return (null, null, false);
        }

        public static string GetAudience(Tenant? tenant)
        {
            var configuredAudience = tenant?.JwtTokenParameters?.Audiences?
                .FirstOrDefault(audience => !string.IsNullOrWhiteSpace(audience));

            if (!string.IsNullOrWhiteSpace(configuredAudience))
            {
                return configuredAudience.Trim();
            }

            return "api://blocks-protected-api";
        }

        public static string GetIssuer(Tenant? tenant)
        {
            var configuredIssuer = tenant?.JwtTokenParameters?.Issuer;
            if (!string.IsNullOrWhiteSpace(configuredIssuer))
            {
                return configuredIssuer.Trim();
            }

            return "selise-blocks";
        }

        private static List<(string domain, string? cookieDomain)> GetTenantDomains(Tenant? tenant)
        {
            var result = new List<(string domain, string? cookieDomain)>();
            if (tenant == null)
            {
                return result;
            }

            // New model: Tenant.Applications[].Domain/CookieDomain
            var applicationsObj = GetPropertyValue(tenant, "Applications");
            if (applicationsObj is IEnumerable applications)
            {
                foreach (var app in applications)
                {
                    if (app == null)
                    {
                        continue;
                    }

                    var appDomain = GetPropertyValue(app, "Domain") as string;
                    if (string.IsNullOrWhiteSpace(appDomain))
                    {
                        continue;
                    }

                    var appCookieDomain = GetPropertyValue(app, "CookieDomain") as string;
                    result.Add((appDomain, string.IsNullOrWhiteSpace(appCookieDomain) ? appDomain : appCookieDomain));
                }
            }

            return result;
        }

        private static object? GetPropertyValue(object obj, string propertyName)
        {
            try
            {
                var property = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                return property?.GetValue(obj);
            }
            catch
            {
                return null;
            }
        }

        private static (string domain, string? cookieDomain)? FindDomainMatch(List<(string domain, string? cookieDomain)> domains, string? hostToMatch)
        {
            if (string.IsNullOrWhiteSpace(hostToMatch))
            {
                return null;
            }

            var normalizedMatch = NormalizeHost(hostToMatch);
            if (string.IsNullOrWhiteSpace(normalizedMatch))
            {
                return null;
            }

            foreach (var item in domains)
            {
                var normalizedDomain = NormalizeHost(item.domain);
                if (string.IsNullOrWhiteSpace(normalizedDomain))
                {
                    continue;
                }

                if (string.Equals(normalizedDomain, normalizedMatch, StringComparison.OrdinalIgnoreCase)
                    || normalizedMatch.EndsWith($".{normalizedDomain}", StringComparison.OrdinalIgnoreCase))
                {
                    return (normalizedMatch, item.cookieDomain);
                }
            }

            return null;
        }

        private static string NormalizeHost(string? value)
        {
            // Directly use BlocksContext.NormalizeDomain
            return BlocksContext.NormalizeDomain(value ?? string.Empty)?.ToLower(CultureInfo.InvariantCulture) ?? string.Empty;
        }

        public static void ResetToOriginalBlocksContextForImpersonation()
        {
            var bc = BlocksContext.GetContext();

            if (bc == null || string.IsNullOrWhiteSpace(bc.OriginalTenantId) || !bc.Impersonated)
            {
                return;
            }
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: bc.OriginalTenantId, 
                userId: bc.UserId,
                impersonated: false,
                isAuthenticated: bc.IsAuthenticated,
                requestUri: bc.RequestUri,
                roles: bc.Roles,
                permissions: bc.Permissions,
                organizationId: bc.OrganizationId,
                email: bc.Email,
                userName: bc.UserId,
                phoneNumber: bc.PhoneNumber,
                expireOn: bc.ExpireOn,
                displayName: bc.DisplayName,
                oauthToken: bc.OAuthToken,
                originalTenantId: bc.OriginalTenantId,
                applicationDomain: bc.ApplicationDomain,
                impersonationSessionId: bc.ImpersonationSessionId)
            );
        }

        public static CookieOptions CreateCookieOptions(string? cookieDomain, DateTime expiresUtc)
        {
            var isLocal = IsLocalhost();
            // True localhost dev -> host-only cookie (a Domain attribute is invalid
            // for "localhost"). Otherwise scope the cookie to the configured shared
            // parent domain (e.g. ".blocksdevelopers.com") so a cookie set by the
            // IDP host is also sent to the app host on the same site.
            cookieDomain = isLocal ? "localhost" : (string.IsNullOrWhiteSpace(cookieDomain) ? null : cookieDomain);

            return new CookieOptions
            {
                Domain = cookieDomain,
                HttpOnly = true,
                Secure = IsCurrentRequestSecure(),
                // Local dev (loopback or hosts-file on http) -> None.
                // Production https -> Strict: cookies are first-party and secure.
                SameSite = IsCrossOriginHttpFlow() ? SameSiteMode.None : SameSiteMode.Strict,
                Path = "/",
                Expires = expiresUtc == default ? DateTime.UtcNow : expiresUtc
            };
        }

        public static string GetRootDomain(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return host;

            var parts = host.Split('.');

            return parts.Length < 2
                ? host
                : $"{parts[^2]}.{parts[^1]}";
        }

    }
}
