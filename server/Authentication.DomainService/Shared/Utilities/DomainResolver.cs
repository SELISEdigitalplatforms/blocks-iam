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

            var request = _httpContextAccessor?.HttpContext?.Request;

            string? origin = request?.Headers["Origin"].ToString();
            string? referer = request?.Headers["Referer"].ToString();

            if (IsLocalUrl(origin) || IsLocalUrl(referer))
                return true;

            // Fallback: treat as production
            return false;
        }

        private static bool IsLocalUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;
            try
            {
                var uri = new Uri(url);
                // Only genuine loopback hosts count as "local". A non-default port
                // (e.g. a real domain served on :5000) must NOT be treated as
                // localhost - doing so previously forced the auth cookies to be
                // host-only on the IDP host instead of scoped to the shared parent
                // domain, so they never reached the app host.
                if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.Equals("127.0.0.1")
                    || uri.Host.Equals("::1"))
                    return true;
            }
            catch { /* ignore parse errors */ }
            return false;
        }
        public static (string? domain, string? cookieDomain, bool isResolved) ResolveDomain(
            Tenant? tenant,
            HttpRequest? request)
        {
            var domains = GetTenantDomains(tenant);
            if (domains.Count == 0)
            {
                return (null, null, false);
            }

            var effectiveContextDomain = BlocksContext.GetContext().ApplicationDomain ?? BlocksContext.ResolveApplicationDomain(request);
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
                applicationDomain: bc.ApplicationDomain)
            );
        }

        public static CookieOptions CreateCookieOptions(string? cookieDomain, DateTime expiresUtc)
        {
            var isLocalRequest = IsLocalhost();
            // True localhost dev -> host-only cookie (a Domain attribute is invalid
            // for "localhost"). Otherwise scope the cookie to the configured shared
            // parent domain (e.g. ".blocksdevelopers.com") so a cookie set by the
            // IDP host is also sent to the app host on the same site.
            cookieDomain = isLocalRequest ? null : (string.IsNullOrWhiteSpace(cookieDomain) ? null : cookieDomain);

            return new CookieOptions
            {
                Domain = cookieDomain,
                HttpOnly = true,
                Secure = true,
                // This is a cross-origin SSO flow: the SPA fetches the IDP callback
                // from a different origin than the IDP itself, so the auth/refresh
                // cookies must be SameSite=None (which mandates Secure, set above).
                // SameSite=Strict would stop the browser from accepting/sending them
                // on the cross-site flow.
                SameSite = SameSiteMode.None,
                Path = "/",
                Expires = expiresUtc == default ? DateTime.UtcNow : expiresUtc
            };
        }
    }
}
