using Microsoft.AspNetCore.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// URL builders and helpers used by the OIDC endpoints.
    /// Extracted from <c>AuthorizationFlowService</c> to keep the orchestrator lean.
    /// </summary>
    public static class OidcRedirectUrlBuilder
    {
        /// <summary>
        /// Returns the public-facing base URL (scheme + host, optional trailing path)
        /// to use when building device-flow verification URIs. Prefers an explicitly
        /// configured <paramref name="publicBaseUrl"/> so the scheme is not pinned to
        /// the internal transport (e.g. gRPC). Falls back to the current request's
        /// scheme/host otherwise.
        /// </summary>
        public static string ResolvePublicBaseUrl(HttpRequest request, string? publicBaseUrl)
        {
            if (!string.IsNullOrWhiteSpace(publicBaseUrl))
            {
                return publicBaseUrl.TrimEnd('/');
            }

            return $"{request.Scheme}://{request.Host.Value}";
        }
        public static string BuildRedirectUri(string baseUri, IDictionary<string, string> parameters)
        {
            var sb = new StringBuilder(baseUri);
            sb.Append(baseUri.Contains('?') ? '&' : '?');

            foreach (var param in parameters)
            {
                if (string.IsNullOrEmpty(param.Value))
                {
                    continue;
                }

                sb.Append(Uri.EscapeDataString(param.Key));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(param.Value));
                sb.Append('&');
            }

            return sb.ToString().TrimEnd('&');
        }

        public static string BuildLoginUrl(
            string clientId,
            string responseType,
            string redirectUri,
            string scope,
            string state,
            string nonce,
            string codeChallenge,
            string codeChallengeMethod,
            string? tenantId)
        {
            var loginUrl = new StringBuilder("/oidc/login?");
            loginUrl.Append($"client_id={Uri.EscapeDataString(clientId ?? string.Empty)}");
            loginUrl.Append($"&response_type={Uri.EscapeDataString(responseType ?? string.Empty)}");
            loginUrl.Append($"&redirect_uri={Uri.EscapeDataString(redirectUri ?? string.Empty)}");
            loginUrl.Append($"&scope={Uri.EscapeDataString(scope ?? string.Empty)}");
            loginUrl.Append($"&state={Uri.EscapeDataString(state ?? string.Empty)}");
            loginUrl.Append($"&nonce={Uri.EscapeDataString(nonce ?? string.Empty)}");
            loginUrl.Append($"&code_challenge={Uri.EscapeDataString(codeChallenge ?? string.Empty)}");
            loginUrl.Append($"&code_challenge_method={Uri.EscapeDataString(codeChallengeMethod ?? string.Empty)}");

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                loginUrl.Append($"&tenant_id={Uri.EscapeDataString(tenantId)}");
            }

            return loginUrl.ToString();
        }

        public static void TryReadBasicClientAuthentication(HttpRequest request, out string clientId, out string clientSecret)
        {
            clientId = string.Empty;
            clientSecret = string.Empty;

            if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var authHeader)
                || !string.Equals(authHeader.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(authHeader.Parameter))
            {
                return;
            }

            try
            {
                var rawCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(authHeader.Parameter));
                var separatorIndex = rawCredentials.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    return;
                }

                clientId = rawCredentials[..separatorIndex];
                clientSecret = rawCredentials[(separatorIndex + 1)..];
            }
            catch
            {
                clientId = string.Empty;
                clientSecret = string.Empty;
            }
        }

        public static string GetClientIpAddress(HttpRequest request)
        {
            return request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        public static string BuildVerificationUri(string apiBase, string? userCode, string? tenantId)
        {
            var baseUrl = TrimTrailingSlash(apiBase);
            var url = $"{baseUrl}/device";

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                url += "/" + Uri.EscapeDataString(tenantId);
            }

            if (!string.IsNullOrWhiteSpace(userCode))
            {
                url += "?user_code=" + Uri.EscapeDataString(userCode);
            }

            return url;
        }

        public static string BuildVerificationUriComplete(string apiBase, string userCode, string? tenantId)
        {
            if (string.IsNullOrWhiteSpace(userCode))
            {
                throw new ArgumentException("userCode must not be empty", nameof(userCode));
            }

            return BuildVerificationUri(apiBase, userCode, tenantId);
        }

        private static string TrimTrailingSlash(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            return value.EndsWith('/') ? value[..^1] : value;
        }
    }
}
