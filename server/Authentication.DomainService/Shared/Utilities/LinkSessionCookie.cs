using Blocks.Genesis;
using Iam.DomainService.Utilities;
using Microsoft.AspNetCore.Http;

namespace Authentication.DomainService.Utilities;

/// <summary>
/// Writes the restricted signup-link session cookie (<c>blocks-link-session-{tenantId}</c>).
/// Distinct from the IdP session cookie — never a substitute for it (A3 / C4).
/// Spec: HttpOnly, Secure, SameSite=Lax, Path=/.
/// </summary>
public static class LinkSessionCookie
{
    public static void Append(
        HttpRequest request,
        HttpResponse response,
        Tenant? tenant,
        string? tenantId,
        string sessionId,
        DateTime expiresUtc)
    {
        var domain = IdpSessionCookie.ResolveCookieDomain(tenant, request);
        var options = DomainResolver.CreateCookieOptions(domain, expiresUtc);
        options.SameSite = SameSiteMode.Lax;
        options.Secure = true;
        options.HttpOnly = true;
        options.Path = "/";

        response.Cookies.Append(
            IdpConstants.BuildLinkSessionCookieKey(tenantId),
            sessionId,
            options);
    }
}
