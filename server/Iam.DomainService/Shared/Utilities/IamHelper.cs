using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.Utilities
{
    public static class IamHelper
    {
        public static string GetOidcRequestBaseUrl(IHttpContextAccessor? httpContextAccessor)
        {
            var request = httpContextAccessor?.HttpContext?.Request;

            if (request == null || !request.Host.HasValue)
            {
                return string.Empty;
            }

            return $"https://{request.Host}".TrimEnd('/');
        }

        public static string GetOriginOrRefererBaseUrl(IHttpContextAccessor? httpContextAccessor)
        {
            var request = httpContextAccessor?.HttpContext?.Request;

            if (request == null)
            {
                return string.Empty;
            }

            return TryGetBaseUrl(request.Headers["Origin"].ToString())
                   ?? TryGetBaseUrl(request.Headers["Referer"].ToString())
                   ?? string.Empty;
        }

        /// <summary>
        /// Appends the originating application's OIDC context to an activation or recovery
        /// path, so the confirmation page can hand the user back to that application
        /// instead of the IAM root login.
        ///
        /// Falls back to the tenant's default client when the caller had no context of its
        /// own (portal invites). Returns the path unchanged when neither is available —
        /// a context-free link is still a working link, just one that lands on IAM.
        /// </summary>
        public static async Task<string> AppendOidcReturnContextAsync(
            string path,
            string? clientId,
            string? redirectUri,
            IDefaultOidcClientResolver? defaultClientResolver)
        {
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(redirectUri))
            {
                var fallback = defaultClientResolver == null
                    ? null
                    : await defaultClientResolver.GetDefaultClientAsync();

                if (fallback == null)
                {
                    return path;
                }

                clientId = fallback.ClientId;
                redirectUri = fallback.RedirectUri;
            }

            var separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return $"{path}{separator}clientId={Uri.EscapeDataString(clientId)}"
                   + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}";
        }

        public static bool TryBuildUserActionUrl(
            IamConfiguration config,
            string path,
            out string url,
            IHttpContextAccessor? httpContextAccessor = null,
            ILogger? logger = null)
        {
            url = string.Empty;

            ArgumentNullException.ThrowIfNull(config);

            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var actionBaseUrl = ResolveActionBaseUrl(config, httpContextAccessor, logger);

            if (string.IsNullOrWhiteSpace(actionBaseUrl))
            {
                return false;
            }

            var baseUrl = actionBaseUrl.TrimEnd('/');

            var normalizedPath = path.StartsWith("/", StringComparison.Ordinal)
                ? path
                : "/" + path;

            url = baseUrl + normalizedPath;

            return true;
        }

        private static string ResolveActionBaseUrl(
            IamConfiguration config,
            IHttpContextAccessor? httpContextAccessor,
            ILogger? logger)
        {
            if (config.UseAccountActionBaseUrlAsDefault && !string.IsNullOrWhiteSpace(config.AccountActionBaseUrl))
            {
                return config.AccountActionBaseUrl;
            }

            if (config.IsOidcEnabled)
            {
                var requestBaseUrl = GetOidcRequestBaseUrl(httpContextAccessor);

                if (!string.IsNullOrWhiteSpace(requestBaseUrl))
                {
                    return requestBaseUrl;
                }

                logger?.LogWarning("OIDC is enabled but request base URL could not be determined. Falling back to AccountActionBaseUrl.");
            }

            var originOrRefererBaseUrl = GetOriginOrRefererBaseUrl(httpContextAccessor);

            if (!string.IsNullOrWhiteSpace(originOrRefererBaseUrl))
            {
                return originOrRefererBaseUrl;
            }

            return config.AccountActionBaseUrl;
        }

        private static string? TryGetBaseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return null;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return $"{uri.Scheme}://{uri.Authority}".TrimEnd('/');
        }
    }
}