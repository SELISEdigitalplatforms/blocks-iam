using Blocks.Genesis;
using Iam.DomainService.Utilities;
using Microsoft.AspNetCore.Http;

namespace Authentication.DomainService.Utilities
{
    /// <summary>
    /// The single place that writes and clears the IdP session cookie
    /// (<c>idp_session_id_{tenantId}</c>).
    ///
    /// A browser only removes a stored cookie when the expiring Set-Cookie matches name, Domain and
    /// Path exactly. Before this helper existed the cookie was written by four call sites using three
    /// different Domain sources, and the single delete never matched some of them, so a stale cookie
    /// survived logout and the next /authorize silently signed the previous user back in.
    ///
    /// Every writer now goes through <see cref="ResolveCookieDomain"/> exactly once, and
    /// <see cref="Clear"/> additionally sweeps the historical scopes so cookies written by older
    /// builds are removed as well.
    /// </summary>
    public static class IdpSessionCookie
    {
        public static void Append(HttpRequest request, HttpResponse response, Tenant? tenant, string? tenantId, string sessionId, DateTime expiresUtc)
        {
            response.Cookies.Append(
                IdpConstants.BuildIdpSessionCookieKey(tenantId),
                sessionId,
                DomainResolver.CreateCookieOptions(ResolveCookieDomain(tenant, request), expiresUtc));
        }

        /// <summary>
        /// Emits one expiring Set-Cookie per scope the cookie may have been stored under: host-only,
        /// the scope <see cref="Append"/> uses, the configured cookie domain, the application host,
        /// and the root-collapsed application host.
        /// </summary>
        public static void Clear(HttpRequest request, HttpResponse response, Tenant? tenant, string? tenantId)
        {
            var cookieKey = IdpConstants.BuildIdpSessionCookieKey(tenantId);
            var expired = DateTime.UtcNow.AddDays(-1);

            foreach (var domain in ResolveClearScopes(tenant, request))
            {
                response.Cookies.Delete(cookieKey, DomainResolver.CreateCookieOptions(domain, expired));
            }
        }

        /// <summary>
        /// The Domain the IdP session cookie is written under.
        ///
        /// An explicitly configured <c>Applications[].CookieDomain</c> wins, matching the access and
        /// refresh token cookies. Without one, a root tenant's cookie collapses to the root domain of
        /// the application host, preserving the cross-subdomain SSO the /authorize writer always
        /// provided for root-tenant applications. Any other tenant gets the application host, and an
        /// unresolvable request gets a host-only cookie.
        /// </summary>
        public static string? ResolveCookieDomain(Tenant? tenant, HttpRequest request)
        {
            var (domain, cookieDomain, isResolved) = DomainResolver.ResolveDomain(tenant, request);
            if (!isResolved)
            {
                return null;
            }

            var cookieDomainIsExplicit = !string.Equals(cookieDomain, domain, StringComparison.OrdinalIgnoreCase);
            if (!cookieDomainIsExplicit && tenant?.IsRootTenant == true && !string.IsNullOrWhiteSpace(domain))
            {
                return DomainResolver.GetRootDomain(domain);
            }

            return cookieDomain;
        }

        public static IReadOnlyList<string?> ResolveClearScopes(Tenant? tenant, HttpRequest request)
        {
            var scopes = new List<string?> { null };
            AddScope(scopes, ResolveCookieDomain(tenant, request));

            var (domain, cookieDomain, isResolved) = DomainResolver.ResolveDomain(tenant, request);
            if (isResolved)
            {
                AddScope(scopes, cookieDomain);
                AddScope(scopes, domain);
                AddScope(scopes, DomainResolver.GetRootDomain(domain ?? string.Empty));
            }

            return scopes;
        }

        private static void AddScope(List<string?> scopes, string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            if (!scopes.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                scopes.Add(candidate);
            }
        }
    }
}
