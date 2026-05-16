using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using System.Globalization;

namespace Authentication.DomainService.Utilities
{
    public static class TenantDomainPolicy
    {
        public static string GetAudience(Tenant? tenant)
        {
            var configuredAudience = tenant?.JwtTokenParameters?.Audiences?
                .FirstOrDefault(audience => !string.IsNullOrWhiteSpace(audience));

            if (!string.IsNullOrWhiteSpace(configuredAudience))
            {
                return configuredAudience.Trim();
            }

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

            // Allow localhost origins in development
            if (IsLocalhostOrigin(requestOriginHost))
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
                // Return host:port format to match actual request origin
                var host = originUri.Host.ToLower(CultureInfo.InvariantCulture);
                // if (originUri.Port != 80 && originUri.Port != 443)
                // {
                //     return $"{host}:{originUri.Port}";
                // }
                return host;
            }

            var referer = request.Headers.Referer.ToString();
            if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri) && !string.IsNullOrWhiteSpace(refererUri.Host))
            {
                var host = refererUri.Host.ToLower(CultureInfo.InvariantCulture);
                // if (refererUri.Port != 80 && refererUri.Port != 443)
                // {
                //     return $"{host}:{refererUri.Port}";
                // }
                return host;
            }

            return string.Empty;
        }

        private static bool IsLocalhostOrigin(string? originHost)
        {
            if (string.IsNullOrWhiteSpace(originHost))
            {
                return false;
            }

            var trimmedHost = originHost.Trim();
            
            // Check for localhost with or without port
            // Handle formats: localhost, localhost:5000, 127.0.0.1, 127.0.0.1:5000, [::1], [::1]:5000
            if (trimmedHost.StartsWith("["))
            {
                // IPv6 format: [::1] or [::1]:5000
                return trimmedHost.Contains("::1");
            }

            var host = trimmedHost.Contains(':') 
                ? trimmedHost.Substring(0, trimmedHost.IndexOf(':')) 
                : trimmedHost;

            host = host.Trim().ToLower();
            
            return host == "localhost" || host == "127.0.0.1" || host == "::1";
        }
    }
}
