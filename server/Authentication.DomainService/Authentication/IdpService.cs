using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Utilities;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// IDP Service
    /// Manages identity provider authentication flow, token exchange, and OIDC operations
    /// </summary>
    public class IdpService : IIdpService
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly IHttpService _httpService;
        private readonly ITenants _tenants;
        private readonly ILogger<IdpService> _logger;

        public IdpService(
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            IHttpService httpService,
            ITenants tenants,
            ILogger<IdpService> logger)
        {
            _authenticationRepository = authenticationRepository;
            _cacheClient = cacheClient;
            _httpService = httpService;
            _tenants = tenants;
            _logger = logger;
        }

        public async Task<IActionResult> StartAuthenticationFlowAsync()
        {
            try
            {
                var effectiveTenantId = BlocksContext.GetContext()?.TenantId;

                // Provider is always the IDP's own OIDC provider — not taken from FE
                var providerName = "idp";
                var providerType = "oidc";

                // Get identity provider config by both provider name and type for exact match
                var identityProvider = await _authenticationRepository.GetIdentityProviderAsync(providerName, providerType);
                if (identityProvider == null || !identityProvider.IsActive)
                {
                    _logger.LogWarning($"Identity provider not found or inactive: {providerName}");
                    return new BadRequestObjectResult(new { error = "invalid_provider", error_description = "Provider not found or not active" });
                }

                // Generate OIDC flow parameters
                var state = GenerateRandomBase64Url(16);
                var nonce = GenerateRandomBase64Url(16);
                var codeVerifier = identityProvider.RequirePkce ? GenerateRandomBase64Url(32) : null;
                var codeChallenge = codeVerifier != null ? GenerateCodeChallenge(codeVerifier) : null;

                // Store flow context in cache (10 minute TTL)
                var flowContext = new
                {
                    state,
                    nonce,
                    codeVerifier,
                    provider = providerName,
                    tenantId = effectiveTenantId,
                    clientId = identityProvider.ClientId,
                    redirectUri = identityProvider.RedirectUri,
                    createdAt = DateTime.UtcNow
                };
                var cacheKey = $"idp_flow:{state}";
                await _cacheClient.AddStringValueAsync(cacheKey, System.Text.Json.JsonSerializer.Serialize(flowContext), 600);

                // Build authorization URL
                var authorizeUrl = BuildAuthorizeUrl(identityProvider, state, nonce, codeChallenge);

                _logger.LogInformation($"Started authentication flow for provider {providerName} with state {state}");

                // Return authorize URL - Frontend will redirect to IdP
                return new OkObjectResult(new { redirect_uri = authorizeUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting authentication flow");
                return new ObjectResult(new { error = "server_error", error_description = "Failed to start authentication flow" })
                {
                    StatusCode = 500
                };
            }
        }

        public async Task<IActionResult> HandleCallbackAsync(string? code, string? state, string? error, string? error_description, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            try
            {
                // Check for IdP errors
                if (!string.IsNullOrWhiteSpace(error))
                {
                    _logger.LogWarning($"IdP returned error: {error}, description: {error_description}");
                    return new BadRequestObjectResult(new
                    {
                        error = error,
                        error_description = error_description ?? "Authorization failed at provider"
                    });
                }

                // Validate authorization code and state
                if (string.IsNullOrWhiteSpace(code))
                {
                    _logger.LogWarning("Callback received without authorization code");
                    return new BadRequestObjectResult(new { error = "invalid_request", error_description = "code is required" });
                }

                if (string.IsNullOrWhiteSpace(state))
                {
                    _logger.LogWarning("Callback received without state parameter");
                    return new BadRequestObjectResult(new { error = "invalid_request", error_description = "state is required" });
                }

                // Retrieve flow context from cache
                var cacheKey = $"idp_flow:{state}";
                var flowContextJson = await _cacheClient.GetStringValueAsync(cacheKey);

                if (string.IsNullOrWhiteSpace(flowContextJson))
                {
                    _logger.LogWarning($"Flow context not found or expired for state: {state}");
                    return new BadRequestObjectResult(new { error = "invalid_state", error_description = "State not found or expired (5 minute timeout)" });
                }

                // Deserialize flow context
                var flowContext = System.Text.Json.JsonSerializer.Deserialize<FlowContext>(flowContextJson);
                if (flowContext == null)
                {
                    _logger.LogWarning($"Failed to deserialize flow context for state: {state}");
                    return new BadRequestObjectResult(new { error = "server_error", error_description = "Invalid flow context" });
                }

                if (string.IsNullOrWhiteSpace(flowContext.Provider))
                {
                    _logger.LogWarning($"Flow context missing provider for state: {state}");
                    return new BadRequestObjectResult(new { error = "invalid_provider", error_description = "Provider missing in flow context" });
                }

                // Get IdP config
                var identityProvider = await _authenticationRepository.GetIdentityProviderAsync(flowContext.Provider);
                if (identityProvider == null || !identityProvider.IsActive)
                {
                    _logger.LogWarning($"Identity provider not found or inactive: {flowContext.Provider}");
                    return new BadRequestObjectResult(new { error = "invalid_provider", error_description = "Provider not configured" });
                }

                // Exchange authorization code for tokens at IdP
                var tokenEndpoint = identityProvider.TokenUrl;
                var form = new Dictionary<string, string>
                {
                    { "grant_type", "authorization_code" },
                    { "code", code },
                    { "client_id", identityProvider.ClientId ?? string.Empty },
                    { "client_secret", identityProvider.ClientSecret ?? string.Empty },
                    { "redirect_uri", identityProvider.RedirectUri ?? string.Empty }
                };

                // Add PKCE code_verifier if present
                if (!string.IsNullOrWhiteSpace(flowContext.CodeVerifier))
                {
                    form["code_verifier"] = flowContext.CodeVerifier;
                }

                OidcTokenEndpointResponse? tokenResponse;
                string tokenError;
                try
                {
                    var timeoutSeconds = (int)GetOutboundRequestTimeout().TotalSeconds;
                    (tokenResponse, tokenError) = await ExchangeCodeForTokenAsync(
                        tokenEndpoint,
                        form,
                        httpRequest.HttpContext.RequestAborted,
                        timeoutSeconds: timeoutSeconds);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Token exchange request failed");
                    return new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Failed to exchange authorization code" });
                }

                if (!string.IsNullOrWhiteSpace(tokenError) || tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
                {
                    _logger.LogWarning($"Token exchange failed: {tokenError ?? "empty token response"}");
                    return new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Failed to exchange authorization code" });
                }

                // Resolve tenant_id: flowContext > BlocksContext > default
                var resolvedTenantId = flowContext.TenantId ?? BlocksContext.GetContext()?.TenantId ?? string.Empty;
                
                var cookieDomain = _tenants.GetTenantByID(resolvedTenantId)?.CookieDomain;
                var tokenResponseObj = new TokenResponse
                {
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    IdToken = tokenResponse.IdToken,
                    TokenType = tokenResponse.TokenType ?? "Bearer",
                    ExpiresUtc = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn ?? 3600),
                    RefreshExpiresUtc = DateTime.UtcNow.AddDays(30),
                    Scope = tokenResponse.Scope,
                    CookieDomain = cookieDomain
                };

                // Always use secure cookies for token delivery in IdP callback flow.
                AppendCookies(tokenResponseObj, httpResponse, resolvedTenantId);

                // Clear cache entry
                await _cacheClient.RemoveKeyAsync(cacheKey);

                _logger.LogInformation($"Successfully completed authentication flow for state: {state}");

                return new OkObjectResult(new
                {
                    token_type = tokenResponseObj.TokenType ?? "Bearer",
                    expires_in = (tokenResponseObj.ExpiresUtc - DateTime.UtcNow).TotalSeconds,
                    scope = tokenResponseObj.Scope,
                    tenant_id = resolvedTenantId,
                    client_id = identityProvider.ClientId,
                    cookie_set = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling IdP callback");
                return new ObjectResult(new { error = "server_error", error_description = "Authentication failed" })
                {
                    StatusCode = 500
                };
            }
        }

        private string GenerateRandomBase64Url(int byteLength)
        {
            var randomBytes = new byte[byteLength];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }
            return Base64UrlEncode(randomBytes);
        }

        private string GenerateCodeChallenge(string codeVerifier)
        {
            var verifierBytes = Encoding.UTF8.GetBytes(codeVerifier);
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var digestBytes = sha256.ComputeHash(verifierBytes);
                return Base64UrlEncode(digestBytes);
            }
        }

        private string Base64UrlEncode(byte[] data)
        {
            var base64 = Convert.ToBase64String(data);
            return base64.Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }

        private static void AppendCookies(TokenResponse response, HttpResponse httpResponse, string? tenantId = null)
        {
            var resolvedTenantId = string.IsNullOrWhiteSpace(tenantId)
                ? BlocksContext.GetContext()?.TenantId ?? "default"
                : tenantId;
            var accessCookieOptions = CreateCookieOptions(response.CookieDomain, response.ExpiresUtc);
            var idCookieOptions = CreateCookieOptions(response.CookieDomain, response.ExpiresUtc);
            var refreshCookieOptions = CreateCookieOptions(response.CookieDomain, response.RefreshExpiresUtc);

            if (!string.IsNullOrWhiteSpace(response.AccessToken))
            {
                httpResponse.Cookies.Append($"{IdpConstants.AccessTokenCookieName}_{resolvedTenantId}", response.AccessToken, accessCookieOptions);
            }

            if (!string.IsNullOrWhiteSpace(response.RefreshToken))
            {
                httpResponse.Cookies.Append($"{IdpConstants.RefreshTokenCookieName}_{resolvedTenantId}", response.RefreshToken, refreshCookieOptions);
            }

            if (!string.IsNullOrWhiteSpace(response.IdToken))
            {
                httpResponse.Cookies.Append($"{IdpConstants.IdTokenCookieName}_{resolvedTenantId}", response.IdToken, idCookieOptions);
            }
        }

        private static CookieOptions CreateCookieOptions(string? domain, DateTime expiresUtc)
        {
            var cookieDomain = IsLocalhost() ? null : (string.IsNullOrWhiteSpace(domain) ? null : domain);
            var isSecure = !IsLocalhost();
            var sameSite = isSecure ? SameSiteMode.Strict : SameSiteMode.None;

            return new CookieOptions
            {
                Domain = cookieDomain,
                HttpOnly = true,
                Secure = isSecure,
                SameSite = sameSite,
                Path = "/",
                Expires = expiresUtc == default ? DateTime.UtcNow.AddHours(1) : expiresUtc
            };
        }

        private static bool IsLocalhost()
        {
            var hostEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "";
            return hostEnv.Equals("Development", StringComparison.OrdinalIgnoreCase);
        }

        private static TimeSpan GetOutboundRequestTimeout()
        {
            return IsLocalhost() ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(100);
        }

        private async Task<(OidcTokenEndpointResponse? Response, string Error)> ExchangeCodeForTokenAsync(
            string tokenEndpoint,
            Dictionary<string, string> form,
            CancellationToken cancellationToken,
            int? timeoutSeconds = null)
        {
            (var response, var error) = await _httpService.SendFormUrlEncoded<OidcTokenEndpointResponse>(
                HttpMethod.Post,
                form,
                tokenEndpoint,
                cancellationToken: cancellationToken,
                timeoutSeconds: timeoutSeconds);

            if (!string.IsNullOrWhiteSpace(error))
            {
                return (null, error);
            }

            return (response, error);
        }

        private string BuildAuthorizeUrl(IdentityProvider provider, string state, string nonce, string? codeChallenge)
        {
            var queryParams = new Dictionary<string, string>
            {
                { "client_id", provider.ClientId ?? string.Empty },
                { "response_type", provider.ResponseType ?? "code" },
                { "redirect_uri", provider.RedirectUri ?? string.Empty },
                { "scope", provider.Scope ?? "openid profile email" },
                { "state", state },
                { "nonce", nonce }
            };

            if (provider.RequirePkce && !string.IsNullOrEmpty(codeChallenge))
            {
                queryParams["code_challenge"] = codeChallenge;
                queryParams["code_challenge_method"] = "S256";
            }

            var queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
            var baseUrl = provider.AuthorizationUrl ?? string.Empty;
            var separator = baseUrl.EndsWith("?") || baseUrl.EndsWith("&")
                ? string.Empty
                : (baseUrl.Contains('?') ? "&" : "?");

            return $"{baseUrl}{separator}{queryString}";
        }

        private sealed class OidcTokenEndpointResponse
        {
            [JsonPropertyName("access_token")]
            public string? AccessToken { get; set; }

            [JsonPropertyName("refresh_token")]
            public string? RefreshToken { get; set; }

            [JsonPropertyName("id_token")]
            public string? IdToken { get; set; }

            [JsonPropertyName("token_type")]
            public string? TokenType { get; set; }

            [JsonPropertyName("expires_in")]
            public int? ExpiresIn { get; set; }

            [JsonPropertyName("scope")]
            public string? Scope { get; set; }
        }

        private sealed class FlowContext
        {
            [JsonPropertyName("state")]
            public string? State { get; set; }

            [JsonPropertyName("nonce")]
            public string? Nonce { get; set; }

            [JsonPropertyName("codeVerifier")]
            public string? CodeVerifier { get; set; }

            [JsonPropertyName("provider")]
            public string? Provider { get; set; }

            [JsonPropertyName("tenantId")]
            public string? TenantId { get; set; }

            [JsonPropertyName("clientId")]
            public string? ClientId { get; set; }

            [JsonPropertyName("redirectUri")]
            public string? RedirectUri { get; set; }

            [JsonPropertyName("createdAt")]
            public DateTime CreatedAt { get; set; }
        }
    }
}
