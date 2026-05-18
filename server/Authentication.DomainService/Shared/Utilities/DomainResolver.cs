using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using System.Collections;
using System.Globalization;
using System.Net;
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
        public static bool IsLocalhost(HttpRequest? request = null)
        {
            // Check environment variable first
            var hostEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? string.Empty;
            if (hostEnv.Equals("Development", StringComparison.OrdinalIgnoreCase))
                return true;

            // If HttpRequest is provided, check Origin and Referer headers
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
                if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.Equals("127.0.0.1")
                    || uri.Host.Equals("::1"))
                    return true;
                if (!uri.IsDefaultPort)
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

            // 1) Prefer BlocksContext app domain.
            var effectiveContextDomain = ResolveBlocksContextDomain();
            if (!string.IsNullOrWhiteSpace(effectiveContextDomain))
            {
                var matched = FindDomainMatch(domains, effectiveContextDomain);
                if (matched != null)
                {
                    return (matched.Value.domain, matched.Value.cookieDomain, true);
                }
            }

            // 2) Try request Origin/Referer host.
            var requestOriginHost = request?.HttpContext != null ? GetRequestOriginHost(request) : null;
            if (!string.IsNullOrWhiteSpace(requestOriginHost))
            {
                var matched = FindDomainMatch(domains, requestOriginHost);
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

        private static string? ResolveBlocksContextDomain()
        {
            try
            {
                var context = BlocksContext.GetContext();
                if (context == null)
                {
                    return null;
                }

                var property = context.GetType().GetProperty("ApplicationDomain");
                var value = property?.GetValue(context) as string;
                return string.IsNullOrWhiteSpace(value) ? null : value;
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

        private static string? GetRequestOriginHost(HttpRequest request)
        {
            var origin = request.Headers.Origin.ToString();
            if (Uri.TryCreate(origin, UriKind.Absolute, out var originUri) && !string.IsNullOrWhiteSpace(originUri.Host))
            {
                var host = originUri.Host.ToLower(CultureInfo.InvariantCulture);
                if (IsLocalhostHost(host))
                {
                    return originUri.Port != 80 && originUri.Port != 443
                        ? $"localhost:{originUri.Port}"
                        : "localhost";
                }

                if (originUri.Port != 80 && originUri.Port != 443)
                {
                    return $"{host}:{originUri.Port}";
                }

                return host;
            }

            var referer = request.Headers.Referer.ToString();
            if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri) && !string.IsNullOrWhiteSpace(refererUri.Host))
            {
                var host = refererUri.Host.ToLower(CultureInfo.InvariantCulture);
                if (IsLocalhostHost(host))
                {
                    return refererUri.Port != 80 && refererUri.Port != 443
                        ? $"localhost:{refererUri.Port}"
                        : "localhost";
                }

                if (refererUri.Port != 80 && refererUri.Port != 443)
                {
                    return $"{host}:{refererUri.Port}";
                }

                return host;
            }

            return null;
        }

        public static bool IsLocalhostHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IPAddress.TryParse(host, out var ipAddress) && IPAddress.IsLoopback(ipAddress);
        }

        private static string NormalizeHost(string? value)
        {
            // Directly use BlocksContext.NormalizeDomain
            return BlocksContext.NormalizeDomain(value ?? string.Empty)?.ToLower(CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
}
