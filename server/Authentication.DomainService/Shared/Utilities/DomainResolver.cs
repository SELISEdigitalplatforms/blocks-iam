using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using System.Globalization;
using System.Net;

namespace Authentication.DomainService.Utilities
{
    /// <summary>
    /// Resolves application and cookie domain, and builds CookieOptions for auth flows.
    ///
    /// Cookie attributes follow a binary rule based on IsLocalhost():
    ///   - Local/dev  : HttpOnly only, no Secure, no SameSite
    ///   - Production : HttpOnly + Secure + SameSite=Strict + resolved Domain
    ///
    /// Domain resolution validates the request's Origin/Referer host against the
    /// tenant's configured Applications[].Domain using exact match (replicates
    /// TenantContextHelper.ResolveApplicationDomain from Genesis).
    /// </summary>
    public static class DomainResolver
    {
        // HttpContextAccessor is required for IsLocalhost() to read RemoteIpAddress
        // (catches hosts-file entries whose hosts resolve to 127.0.0.1).
        private static IHttpContextAccessor? _httpContextAccessor;

        public static void Configure(IHttpContextAccessor accessor)
        {
            _httpContextAccessor = accessor;
        }

        public static bool IsLocalhost()
        {
            // 1) Caller-advertised host is loopback (localhost / 127.0.0.1 / ::1), any port.
            if (TryGetRequestOriginUri(out var uri))
            {
                if (IsLoopbackHost(uri.Host))
                    return true;

                // 2) Non-default port in the Origin/Referer URI - explicit dev setup
                //    (e.g. https://example.com:5001, http://staging.example.com:8080).
                //    Production traffic stays on default ports (443/80).
                if (!uri.IsDefaultPort)
                    return true;
            }

            // 3) Connection came from a loopback IP - catches hosts-file entries
            //    where the browser resolves dev-os.blocksdevelopers.com to 127.0.0.1
            //    and connects directly to the loopback interface.
            var remoteIp = _httpContextAccessor?.HttpContext?.Connection.RemoteIpAddress;
            return remoteIp != null && IPAddress.IsLoopback(remoteIp);
        }

        public static CookieOptions CreateCookieOptions(string? cookieDomain, DateTime expiresUtc)
        {
            return IsLocalhost()
                ? CreateLoopbackCookieOptions(cookieDomain, expiresUtc)
                : CreateProductionCookieOptions(cookieDomain, expiresUtc);
        }

        public static CookieOptions CreateLoopbackCookieOptions(string? cookieDomain, DateTime expiresUtc)
        {
            // Loopback mode (loopback host OR hosts-file entry OR non-default port):
            // HttpOnly only, no Secure, no SameSite.
            // cookieDomain is already resolved by ResolveDomain (validated against
            // tenant.Applications[].Domain), so we trust it.
            return new CookieOptions
            {
                Domain = cookieDomain,
                HttpOnly = true,
                Path = "/",
                Expires = NormalizeExpiry(expiresUtc)
            };
        }

        public static CookieOptions CreateProductionCookieOptions(string? cookieDomain, DateTime expiresUtc)
        {
            // Production: full hardening with resolved domain.
            return new CookieOptions
            {
                Domain = string.IsNullOrWhiteSpace(cookieDomain) ? null : cookieDomain,
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                Expires = NormalizeExpiry(expiresUtc)
            };
        }

        private static DateTime NormalizeExpiry(DateTime expiresUtc) =>
            expiresUtc == default ? DateTime.UtcNow : expiresUtc;

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
            if (tenant?.Applications == null || tenant.Applications.Count == 0)
            {
                return (null, null, false);
            }

            // Replicates TenantContextHelper.ResolveApplicationDomain (internal in Genesis):
            // validate the Origin/Referer host against the tenant's configured applications
            // using exact match. Localhost origins/referers are skipped (resolved by
            // IsLocalhost() at the cookie layer).
            var browserHost = ResolveValidatedBrowserHost(request);
            if (string.IsNullOrWhiteSpace(browserHost))
            {
                return (null, null, false);
            }

            var match = tenant.Applications
                .FirstOrDefault(a => NormalizeHost(a.Domain).Equals(browserHost, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                return (null, null, false);
            }

            var cookieDomain = string.IsNullOrWhiteSpace(match.CookieDomain) ? NormalizeHost(match.Domain) : match.CookieDomain;
            return (NormalizeHost(match.Domain), cookieDomain, true);
        }

        private static string ResolveValidatedBrowserHost(HttpRequest? request)
        {
            if (request?.Headers == null)
            {
                return string.Empty;
            }

            var origin = request.Headers.Origin.ToString();
            var referer = request.Headers.Referer.ToString();

            // Skip localhost origins/referers - they don't carry a valid production domain.
            if (IsLocalhostUri(origin) || IsLocalhostUri(referer))
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(origin)
                && Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            {
                return NormalizeHost(originUri.Host);
            }

            if (!string.IsNullOrWhiteSpace(referer)
                && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
            {
                return NormalizeHost(refererUri.Host);
            }

            return string.Empty;
        }

        private static bool IsLocalhostUri(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                return false;
            }
            var host = uri.Host;
            return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));
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
