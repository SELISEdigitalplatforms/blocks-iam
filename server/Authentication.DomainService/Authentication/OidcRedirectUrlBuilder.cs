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
        /// Returns the base URL (scheme + host) used when building device-flow
        /// verification URIs.
        /// </summary>
        public static string ResolvePublicBaseUrl(HttpRequest request)
        {
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

        /// <summary>
        /// The login page for an OIDC request, carrying an error for the user to read.
        ///
        /// <para>
        /// This is the single landing place for a browser-navigated failure that still knows
        /// which OIDC request it belongs to. The SPA reads <c>error_description</c> (falling
        /// back to <c>error</c>) off this URL and raises the blocking error dialog over the
        /// login card, whose only action hands the user back to the application. Returning a
        /// body instead would leave them reading raw JSON in the address bar.
        /// </para>
        /// <para>
        /// Same-origin by construction, because <see cref="BuildLoginUrl"/> returns a path. That
        /// matters for the <c>invalid_client</c> and unregistered-<c>redirect_uri</c> refusals:
        /// RFC 6749 section 4.1.2.1 forbids redirecting to the client in those cases, and a path
        /// on our own host is not a redirect to the client.
        /// </para>
        /// </summary>
        public static string BuildLoginErrorUrl(
            string clientId,
            string responseType,
            string redirectUri,
            string scope,
            string state,
            string nonce,
            string codeChallenge,
            string codeChallengeMethod,
            string? tenantId,
            string error,
            string errorDescription)
        {
            var loginUrl = BuildLoginUrl(
                clientId,
                responseType,
                redirectUri,
                scope,
                state,
                nonce,
                codeChallenge,
                codeChallengeMethod,
                tenantId);

            return BuildRedirectUri(loginUrl, new Dictionary<string, string>
            {
                { "error", error },
                { "error_description", errorDescription }
            });
        }

        /// <summary>
        /// The standalone error page, for a browser-navigated failure that has lost the OIDC
        /// request it belonged to -- an expired or already-consumed state, say. There is no
        /// login card to raise a dialog over and nothing to send the user back to, so the SPA
        /// renders the error on a page of its own.
        /// </summary>
        public static string BuildErrorPageUrl(string error, string errorDescription, string? tenantId)
        {
            var parameters = new Dictionary<string, string>
            {
                { "error", error },
                { "error_description", errorDescription }
            };

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                parameters["tenant_id"] = tenantId;
            }

            return BuildRedirectUri("/oidc/error", parameters);
        }

        /// <summary>
        /// Link to the signup page for a tenant, used by <c>/api/idp/initiate?flow=signup</c>.
        ///
        /// <para>
        /// Unlike <see cref="BuildLoginUrl"/> this returns an <em>absolute</em> URL. That one
        /// feeds a same-origin redirect out of <c>/api/oidc/authorize</c>, so a path is enough;
        /// this one is handed back to an application on a different origin, which has no way to
        /// resolve a bare path against the IAM host.
        /// </para>
        /// <para>
        /// Parameter spellings are not free choice. <c>extractOIDCParams</c> in the SPA reads
        /// <c>redirect_uri</c> in snake case only, and the activation-email builder emits the
        /// same <c>clientId</c> + <c>redirect_uri</c> pair -- diverging here fails silently,
        /// leaving a signup page that works and an activation email that returns the user to
        /// the wrong application.
        /// </para>
        /// </summary>
        public static string BuildSignupUrl(
            string publicBaseUrl,
            string tenantId,
            string? clientId,
            string redirectUri,
            string scope,
            string state,
            string nonce)
        {
            var baseUrl = TrimTrailingSlash(publicBaseUrl);
            var signupUrl = $"{baseUrl}/oidc/signup/{Uri.EscapeDataString(tenantId)}";

            return BuildRedirectUri(signupUrl, new Dictionary<string, string>
            {
                { "clientId", clientId ?? string.Empty },
                { "redirect_uri", redirectUri },
                { "scope", scope },
                { "state", state },
                { "nonce", nonce },
                { "tenant_id", tenantId }
            });
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
