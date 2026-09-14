using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
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

        /// <summary>
        /// The base URL of this IAM deployment, as configured for the environment
        /// (dev / stg / prod). Read from <c>BLOCKS_IAM_BASE_URL</c>, which the host loads
        /// from the Mongo <c>blocks-secret-iam</c> secret and also bakes into the SPA's
        /// runtime env, so server and client always agree on the same host.
        ///
        /// Returns an empty string when the key is unset or is not an absolute URL;
        /// callers decide their own fallback.
        /// </summary>
        public static string GetConfiguredIamBaseUrl(IConfiguration? configuration)
        {
            var configured = Environment.GetEnvironmentVariable("BLOCKS_IAM_BASE_URL")
                ?? configuration?["BLOCKS_IAM_BASE_URL"]
                ?? configuration?["FrontendRuntime:BLOCKS_IAM_BASE_URL"];

            return TryGetBaseUrl(configured ?? string.Empty) ?? string.Empty;
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
            ILogger? logger = null,
            // Trailing and optional so existing call sites keep compiling. Supplies the
            // deployment's own base URL for the OIDC branch of ResolveActionBaseUrl.
            IConfiguration? appConfiguration = null)
        {
            url = string.Empty;

            ArgumentNullException.ThrowIfNull(config);

            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var actionBaseUrl = ResolveActionBaseUrl(config, httpContextAccessor, appConfiguration, logger);

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
            IConfiguration? appConfiguration,
            ILogger? logger)
        {
            if (config.IsOidcEnabled)
            {
                // Under OIDC the activation and recovery pages are served by IAM itself, so
                // the host is a property of this deployment and never of the caller. Reading
                // it from the request's Host header would let a forged header aim a recovery
                // link at someone else's domain, and would make one tenant produce two
                // different links depending on whether the mail was built in the API (Host
                // present) or in the Worker (no HttpContext at all).
                var deploymentBaseUrl = GetConfiguredIamBaseUrl(appConfiguration);

                if (!string.IsNullOrWhiteSpace(deploymentBaseUrl))
                {
                    return deploymentBaseUrl;
                }

                // The config save writes BLOCKS_IAM_BASE_URL here whenever OIDC is on, so
                // this is normally the same value; it also covers rows saved before that.
                if (!string.IsNullOrWhiteSpace(config.AccountActionBaseUrl))
                {
                    return config.AccountActionBaseUrl;
                }

                logger?.LogWarning(
                    "OIDC is enabled but neither BLOCKS_IAM_BASE_URL nor AccountActionBaseUrl "
                    + "is set. Falling back to the request host.");

                return GetOidcRequestBaseUrl(httpContextAccessor);
            }

            if (config.UseAccountActionBaseUrlAsDefault && !string.IsNullOrWhiteSpace(config.AccountActionBaseUrl))
            {
                return config.AccountActionBaseUrl;
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