using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Utilities;
using Blocks.CaptchaDriver;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// IDP Service
    /// Manages identity provider authentication flow, token exchange, and OIDC operations
    /// </summary>
    public sealed class IdpService : IIdpService
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IAuthorizationCodeRepository _authCodeRepo;
        private readonly IAuthenticationFlowService _authenticationFlowService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ICacheClient _cacheClient;
        private readonly IHttpService _httpService;
        private readonly ITenants _tenants;
        private readonly ICaptchaConfigurationRepository _captchaConfigurationRepository;
        private readonly ILogger<IdpService> _logger;

        public IdpService(
            IAuthenticationRepository authenticationRepository,
            IAuthorizationCodeRepository authCodeRepo,
            IAuthenticationFlowService authenticationFlowService,
            IHttpContextAccessor httpContextAccessor,
            ICacheClient cacheClient,
            IHttpService httpService,
            ITenants tenants,
            ICaptchaConfigurationRepository captchaConfigurationRepository,
            ILogger<IdpService> logger)
        {
            _authenticationRepository = authenticationRepository;
            _authCodeRepo = authCodeRepo;
            _authenticationFlowService = authenticationFlowService;
            _httpContextAccessor = httpContextAccessor;
            _cacheClient = cacheClient;
            _httpService = httpService;
            _tenants = tenants;
            _captchaConfigurationRepository = captchaConfigurationRepository;
            _logger = logger;
        }

        public async Task<IActionResult> GetUiConfigAsync()
        {
            var captchaConfiguration = await _captchaConfigurationRepository.GetCaptchaConfigurationAsync();

            return new OkObjectResult(new
            {
                Captcha = captchaConfiguration == null || !captchaConfiguration.IsEnable ? null : new
                {
                    Key = captchaConfiguration.CaptchaKey,
                    Provider = captchaConfiguration.Provider,
                    Generator = captchaConfiguration.CaptchaGenerator
                }

            });
        }

        public async Task<IActionResult> StartAuthenticationFlowAsync(string clientId, string redirectUri, string? forwardedTo)
        {
            try
            {
                var effectiveTenantId = BlocksContext.GetContext()?.TenantId;

                // One ClientId maps to one provider entry.
                var identityProvider = await _authenticationRepository.GetIdentityProviderByClientIdAsync(clientId);
                if (identityProvider == null || !identityProvider.IsActive)
                {
                    return new BadRequestObjectResult(new { error = "invalid_client", error_description = "ClientId not found or inactive" });
                }

                if (string.IsNullOrWhiteSpace(redirectUri))
                {
                    return new BadRequestObjectResult(new { error = "invalid_request", error_description = "redirectUri is required" });
                }

                var allowedRedirectUris = identityProvider.RedirectUris ?? [];
                if (!allowedRedirectUris.Any(x => string.Equals(x, redirectUri, StringComparison.OrdinalIgnoreCase)))
                {
                    return new BadRequestObjectResult(new { error = "invalid_redirect_uri", error_description = "redirectUri is not registered for this client" });
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
                    provider = identityProvider.Provider,
                    tenantId = effectiveTenantId,
                    clientId = identityProvider.ClientId,
                    redirectUri,
                    createdAt = DateTime.UtcNow,
                    forwardedTo = forwardedTo
                };
                var cacheKey = $"idp_flow:{state}";
                await _cacheClient.AddStringValueAsync(cacheKey, System.Text.Json.JsonSerializer.Serialize(flowContext), 600);

                // Build authorization URL
                var authorizeUrl = BuildAuthorizeUrl(identityProvider, redirectUri, state, nonce, codeChallenge);

                _logger.LogInformation("Started authentication flow for provider {Provider} with state {State}", identityProvider.Provider, state);

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
                    _logger.LogWarning("IdP returned error: {Error}, description: {ErrorDescription}", error, error_description);
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
                    _logger.LogWarning("Flow context not found or expired for state: {State}", state);
                    return new BadRequestObjectResult(new { error = "invalid_state", error_description = "State not found or expired (5 minute timeout)" });
                }

                // Deserialize flow context
                var flowContext = System.Text.Json.JsonSerializer.Deserialize<FlowContext>(flowContextJson);
                if (flowContext == null)
                {
                    _logger.LogWarning("Failed to deserialize flow context for state: {State}", state);
                    return new BadRequestObjectResult(new { error = "server_error", error_description = "Invalid flow context" });
                }

                if (string.IsNullOrWhiteSpace(flowContext.Provider))
                {
                    _logger.LogWarning("Flow context missing provider for state: {State}", state);
                    return new BadRequestObjectResult(new { error = "invalid_provider", error_description = "Provider missing in flow context" });
                }

                // Get IdP config
                var identityProvider = await _authenticationRepository.GetIdentityProviderAsync(flowContext.Provider);
                if (identityProvider == null || !identityProvider.IsActive)
                {
                    _logger.LogWarning("Identity provider not found or inactive: {Provider}", flowContext.Provider);
                    return new BadRequestObjectResult(new { error = "invalid_provider", error_description = "Provider not configured" });
                }

                // Exchange authorization code for tokens at IdP
                var tokenEndpoint = identityProvider.TokenUrl;
 
                var form = new Dictionary<string, string>
                {
                    { "grant_type", GrantTypes.AuthCode },
                    { "code", code },
                    { "client_id", identityProvider.ClientId ?? string.Empty },
                    { "client_secret", identityProvider.ClientSecret ?? string.Empty },
                    { "redirect_uri", flowContext.RedirectUri ?? string.Empty }
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
                    _logger.LogWarning("Token exchange failed: {TokenError}", tokenError ?? "empty token response");
                    return new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Failed to exchange authorization code" });
                }

                var authCode = await _authCodeRepo.GetByCodeAsync(code);

                if (authCode.Impersonated)
                {
                    var impersonatedResult = await _authenticationFlowService.ExecuteImpersonateAsync(
                        new ImpersonateRequest 
                        { 
                            TargetTenantId = authCode.TargetedTenantId, 
                            ImpersontingUserId = authCode.ImpersonatedUserId, 
                            RefreshToken = tokenResponse.RefreshToken,
                            OrganizationId = authCode.OrganizationId,
                        }, 
                        _httpContextAccessor.HttpContext.Request, 
                        _httpContextAccessor.HttpContext.Response
                    );

                    await _cacheClient.RemoveKeyAsync(cacheKey); 
                    return new OkObjectResult(new { Impersonated = true });
                }

                // Resolve tenant_id: flowContext > BlocksContext > default
                var blocksContext = BlocksContext.GetContext();
                var resolvedTenantId = flowContext.TenantId ?? blocksContext?.TenantId ?? string.Empty;
                var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
                var configuredAccessLifetimeSeconds = Math.Max((authConfiguration?.AccessTokenValidForNumberMinutes ?? IdentityConfiguration.DefaultAccessTokenValidForNumberMinutes) * 60, 60);
                var configuredRefreshLifetimeMinutes = Math.Max(authConfiguration?.AbsoluteRefreshTokenValidForNumberMinutes ?? IdentityConfiguration.DefaultRememberMeRefreshTokenValidForNumberMinutes, 1);
                var resolvedAccessLifetimeSeconds = tokenResponse.ExpiresIn.HasValue
                    ? Math.Max(tokenResponse.ExpiresIn.Value, 60)
                    : configuredAccessLifetimeSeconds;

                var tenant = _tenants.GetTenantByID(resolvedTenantId);
                var (domain, cookieDomain, isResolved) = DomainResolver.ResolveDomain(tenant, httpRequest);
                var tokenResponseObj = new TokenResponse
                {
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    IdToken = tokenResponse.IdToken,
                    TokenType = tokenResponse.TokenType ?? "Bearer",
                    ExpiresUtc = DateTime.UtcNow.AddSeconds(resolvedAccessLifetimeSeconds),
                    RefreshExpiresUtc = DateTime.UtcNow.AddMinutes(configuredRefreshLifetimeMinutes),
                    Scope = tokenResponse.Scope,
                    CookieDomain = cookieDomain
                };

                await _cacheClient.RemoveKeyAsync(cacheKey);

                if (isResolved && !string.IsNullOrWhiteSpace(domain))
                {
                    AppendCookies(tokenResponseObj, httpResponse, domain);
                    _logger.LogInformation("Successfully completed authentication flow for state: {State}", state);

                    return new OkObjectResult(new
                    {
                        id_token = tokenResponseObj.IdToken,
                        token_type = tokenResponseObj.TokenType ?? "Bearer",
                        expires_in = (tokenResponseObj.ExpiresUtc - DateTime.UtcNow).TotalSeconds,
                        scope = tokenResponseObj.Scope
                    });
                }


                _logger.LogInformation("Successfully completed authentication flow for state: {State}", state);

                return new OkObjectResult(new
                {
                    access_token = tokenResponseObj.AccessToken,
                    refresh_token = tokenResponseObj.RefreshToken,
                    id_token = tokenResponseObj.IdToken,
                    token_type = tokenResponseObj.TokenType ?? "Bearer",
                    expires_in = (tokenResponseObj.ExpiresUtc - DateTime.UtcNow).TotalSeconds,
                    scope = tokenResponseObj.Scope
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

        private static bool AppendCookies(TokenResponse response, HttpResponse httpResponse, string domain)
        {
            // Validate response has no error indicator
            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                return false;
            }

            // Validate access token is present
            if (string.IsNullOrWhiteSpace(response.AccessToken))
            {
                return false;
            }


            var accessCookieOptions = DomainResolver.CreateCookieOptions(response.CookieDomain, response.ExpiresUtc);
            var refreshCookieOptions = DomainResolver.CreateCookieOptions(response.CookieDomain, response.RefreshExpiresUtc);

            DeleteCookie(httpResponse, domain, accessCookieOptions, refreshCookieOptions);

            httpResponse.Cookies.Append(domain, response.AccessToken, accessCookieOptions);

            if (!string.IsNullOrWhiteSpace(response.RefreshToken))
            {
                httpResponse.Cookies.Append($"{IdpConstants.RefreshTokenCookieName}_{domain}", response.RefreshToken, refreshCookieOptions);
            }

            return true;
        }

        private static void DeleteCookie(HttpResponse httpResponse, string domain, CookieOptions accessCookieOptions, CookieOptions refreshCookieOptions)
        {

            httpResponse.Cookies.Delete(domain, accessCookieOptions);
            httpResponse.Cookies.Delete($"{IdpConstants.RefreshTokenCookieName}_{domain}", refreshCookieOptions);

        }

        private static TimeSpan GetOutboundRequestTimeout()
        {
            return DomainResolver.IsLocalhost() ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(100);
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

        private string BuildAuthorizeUrl(IdentityProvider provider, string redirectUri, string state, string nonce, string? codeChallenge)
        {
            var queryParams = new Dictionary<string, string>
            {
                { "client_id", provider.ClientId ?? string.Empty },
                { "response_type", provider.ResponseType ?? "code" },
                { "redirect_uri", redirectUri },
                { "scope", provider.Scope ?? AuthenticationConstants.OpenIdProfileEmailScope },
                { "state", state },
                { "nonce", nonce }
            };

            if (provider.RequirePkce && !string.IsNullOrEmpty(codeChallenge))
            {
                queryParams["code_challenge"] = codeChallenge;
                queryParams["code_challenge_method"] = AuthenticationConstants.PkceMethodS256;
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

            [JsonPropertyName("forwardedTo")]
            public string? ForwardedTo { get; set; } = null!;
        }
    }
}
