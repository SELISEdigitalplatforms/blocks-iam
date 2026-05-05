using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using System.Globalization;

namespace Authentication.DomainService.Utilities
{
    public static class TenantDomainPolicy
    {
        public static string GetAudience(Tenant? tenant)
        {
            return NormalizeHost(tenant?.ApplicationDomain);
        }

        public static bool IsOriginAllowed(HttpRequest? request, Tenant? tenant)
        {
            if (tenant == null)
            {
                return false;
            }

            if (request?.HttpContext == null)
            {
                return true;
            }

            var requestOriginHost = GetRequestOriginHost(request);
            if (string.IsNullOrWhiteSpace(requestOriginHost))
            {
                return true;
            }

            var allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var allowedDomain in tenant.AllowedDomains ?? [])
            {
                var allowedHost = NormalizeHost(allowedDomain);
                if (!string.IsNullOrWhiteSpace(allowedHost))
                {
                    allowedHosts.Add(allowedHost);
                }
            }

            return allowedHosts.Contains(requestOriginHost);
        }

        public static string NormalizeHost(string? value)
        {
            var domain = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(domain))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(domain, UriKind.Absolute, out var absoluteUri) && !string.IsNullOrWhiteSpace(absoluteUri.Host))
            {
                return absoluteUri.Host.ToLower(CultureInfo.InvariantCulture);
            }

            if (Uri.TryCreate($"https://{domain}", UriKind.Absolute, out var normalizedUri) && !string.IsNullOrWhiteSpace(normalizedUri.Host))
            {
                return normalizedUri.Host.ToLower(CultureInfo.InvariantCulture);
            }

            return domain.TrimEnd('/').ToLower(CultureInfo.InvariantCulture);
        }

        private static string GetRequestOriginHost(HttpRequest request)
        {
            var origin = request.Headers.Origin.ToString();
            if (Uri.TryCreate(origin, UriKind.Absolute, out var originUri) && !string.IsNullOrWhiteSpace(originUri.Host))
            {
                return originUri.Host.ToLower(CultureInfo.InvariantCulture);
            }

            var referer = request.Headers.Referer.ToString();
            if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri) && !string.IsNullOrWhiteSpace(refererUri.Host))
            {
                return refererUri.Host.ToLower(CultureInfo.InvariantCulture);
            }

            return string.Empty;
        }
    }
}
