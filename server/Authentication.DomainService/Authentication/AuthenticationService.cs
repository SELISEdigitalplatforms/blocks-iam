using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Utilities;
using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Authentication.DomainService.Shared;

namespace Authentication.DomainService.Authentication
{
    public class AuthenticationService : IAuthenticationService
    {
        private static readonly HttpClient BackchannelHttpClient = new();
        private const string IdpSessionCookieName = "idp_session_id";
        private readonly ILogger<AuthenticationService> _logger;
        private readonly ICacheClient _cacheClient;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly IIdpSessionService _idpSessionService;
        private readonly ITokenRevocationService _tokenRevocationService;
        private readonly ITenants _tenants;
        private readonly UnifiedTokenSessionService _unifiedTokenSessionService;

        private const string Public_Cert_Cache_Prefix = "tetocertpublic::";

        public AuthenticationService(
            ILogger<AuthenticationService> logger,
            ICacheClient cacheClient,
            IAuthenticationRepository authenticationRepository,
            IAuditLogRepository auditLogRepository,
            IAuthenticationDomainService authenticationDomainService,
            IIdpSessionService idpSessionService,
            ITokenRevocationService tokenRevocationService,
            ITenants tenants,
            UnifiedTokenSessionService unifiedTokenSessionService
        )
        {
            _logger = logger;
            _cacheClient = cacheClient;
            _authenticationRepository = authenticationRepository;
            _auditLogRepository = auditLogRepository;
            _authenticationDomainService = authenticationDomainService;
            _idpSessionService = idpSessionService;
            _tokenRevocationService = tokenRevocationService;
            _tenants = tenants;
            _unifiedTokenSessionService = unifiedTokenSessionService;
        }

        public async Task<IActionResult> BuildFlowResultAsync(AuthenticationFlowResult result, HttpContext httpContext)
        {
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                return new ObjectResult(new
                {
                    error = result.Error,
                    error_description = result.ErrorDescription
                })
                {
                    StatusCode = result.StatusCode
                };
            }

            if (result.TokenResponse == null)
            {
                return new ObjectResult(new
                {
                    error = "server_error",
                    error_description = "Authentication flow returned no response"
                })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }

            return await BuildTokenResponseAsync(result.TokenResponse, httpContext);
        }

        public async Task<bool> UpdateIdpSessionForLogoutAsync(HttpContext httpContext, ClaimsPrincipal user, bool isGlobalLogout)
        {
            var sessionId = httpContext.Request.Cookies[IdpSessionCookieName];
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return true;
            }

            if (isGlobalLogout)
            {
                await _idpSessionService.RevokeSessionAsync(sessionId, "logout_all");
                return true;
            }

            var userId = user.FindFirst(BlocksContext.USER_ID_CLAIM)?.Value ?? user.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            var tenantId = user.FindFirst(BlocksContext.TENANT_ID_CLAIM)?.Value
                ?? user.FindFirst("tenant_id")?.Value
                ?? BlocksContext.GetContext()?.TenantId;

            await _idpSessionService.RemoveAccountAsync(sessionId, userId, tenantId);

            var session = await _idpSessionService.GetSessionAsync(sessionId);
            if (session == null || session.RevokedAt.HasValue || session.IsExpired())
            {
                return true;
            }

            return session.Accounts.Count == 0;
        }

        public void ClearIdpSessionCookie(HttpResponse response)
        {
            response.Cookies.Delete(IdpSessionCookieName);
        }

        public async Task<LogoutResponse> LogoutUser(string refreshToken, HttpRequest httpRequest)
        {
            _logger.LogInformation("Logout process start");

            var isAll = string.IsNullOrWhiteSpace(refreshToken);

            var result = isAll ? await ProcessLogoutAll(httpRequest) : await ProcessLogout(refreshToken, httpRequest);

            await ProcessTimeline(httpRequest, isAll);
            return new LogoutResponse
            {
                IsSuccess = result,
            };

        }

        public async Task<bool> ProcessLogout(string refreshToken, HttpRequest httpRequest)
        {
            var bc = BlocksContext.GetContext();

            // Revoke refresh token family to align with rotation security and prevent sibling token reuse.
            await _tokenRevocationService.RevokeTokenAsync(refreshToken, "refresh_token", string.Empty);
            var revokeResult = await _tokenRevocationService.RevokeTokenAsync(refreshToken, "refresh_token", string.Empty);
            if (!revokeResult.Success)
            {
                _logger.LogWarning("Refresh-token family revocation failed during logout: {Error}", revokeResult.Error ?? "unknown_error");
            }

            await _cacheClient.RemoveKeyAsync(refreshToken);

            var result = await _authenticationRepository.RevokeIdentitySessionAsync(refreshToken, bc?.UserId ?? "");

            return result;
        }

        public async Task<bool> ProcessLogoutAll(HttpRequest httpRequest)
        {
            var bc = BlocksContext.GetContext();

            var refreshTokens = (await _authenticationRepository.GetActiveIdentitySessionByUserIdAsync(bc?.UserId ?? string.Empty)).Select(x => x.RefreshToken).ToList();
            var revokeTasks = refreshTokens.Select(async x => await _unifiedTokenSessionService.RevokeRefreshToken(x));
            await Task.WhenAll(revokeTasks);

            var result = await _authenticationRepository.RevokeIdentitySessionsByRefreshTokensAsync(refreshTokens);
            return result;
        }

        public async Task<bool> ProcessTimeline(HttpRequest httpRequest, bool isFromAll)
        {
            var bc = BlocksContext.GetContext();
            var eventTimeline = new UserAuthenticationTimelineEvent
            {
                DeviceInformation = _authenticationDomainService.GetDeviceInfo(httpRequest?.Headers?.UserAgent ?? string.Empty),
                IpAddresses = string.Join(",", _authenticationDomainService.GetVisitorsIpAddresses(httpRequest?.HttpContext)),
                Event = isFromAll ? "revoke_access_by_logout_all" : "revoke_access_by_logout",
                ActionBy = isFromAll ? "call_api_to_logout_all" : "call_api_to_logout",
                UserId = bc?.UserId ?? ""
            };

            await _authenticationDomainService.SendToQueueAsync(IdpConstants.AuthenticationQueue, eventTimeline);
            return true;
        }

        public async Task<OidcClientRegistration> GetClientCredentialAsync(string clientId)
        {
            return await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
        }

        public string CookieToken(HttpRequest request)
        {
            var bc = BlocksContext.GetContext();
            var tenant = _tenants.GetTenantByID(bc?.TenantId ?? "default");
            var (domain, _, isResolved) = DomainResolver.ResolveDomain(tenant, request);
            if (!isResolved || string.IsNullOrWhiteSpace(domain))
            {
                return string.Empty;
            }

            var cookieKey = $"{IdpConstants.RefreshTokenCookieName}_{domain}";
            var refreshToken = request.HttpContext.Request.Cookies[cookieKey];
            refreshToken = string.IsNullOrEmpty(refreshToken) ? request.HttpContext.Request.Headers[cookieKey] : refreshToken;

            return refreshToken;
        }

        public bool DeleteCookie(HttpRequest request)
        {
            var bc = BlocksContext.GetContext();
            var tenantId = bc?.TenantId ?? "default";
            var tenant = _tenants.GetTenantByID(tenantId);
            var (domain, cookieDomain, isResolved) = DomainResolver.ResolveDomain(tenant, request);
            var cookieOptions = CreateCookieOptions(cookieDomain, DateTime.UtcNow.AddDays(-1));

            if (isResolved && !string.IsNullOrWhiteSpace(domain))
            {
                request.HttpContext.Response.Cookies.Delete($"{IdpConstants.RefreshTokenCookieName}_{domain}", cookieOptions);
                request.HttpContext.Response.Cookies.Delete($"{domain}", cookieOptions);
            }

            // Backward compatibility cleanup for legacy callback cookie names.
            request.HttpContext.Response.Cookies.Delete("oidc_token", cookieOptions);
            request.HttpContext.Response.Cookies.Delete("oidc_refresh_token", cookieOptions);

            return true;
        }

        public async Task AppendSessionCookies(HttpContext httpContext, string? accessToken, string? refreshToken, DateTime? accessExpiresUtc = null, DateTime? refreshExpiresUtc = null)
        {
            var bc = BlocksContext.GetContext();
            var tenantId = bc?.TenantId ?? "default";
            var tenant = _tenants.GetTenantByID(tenantId);
            var (domain, cookieDomain, isResolved) = DomainResolver.ResolveDomain(tenant, httpContext.Request);
            if (!isResolved || string.IsNullOrWhiteSpace(domain))
            {
                return;
            }
            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            var accessLifetimeMinutes = Math.Max(authConfiguration?.AccessTokenValidForNumberMinutes ?? AuthenticationConfiguration.DefaultAccessTokenValidForNumberMinutes, 1);
            var refreshLifetimeMinutes = Math.Max(authConfiguration?.AbsoluteRefreshTokenValidForNumberMinutes ?? AuthenticationConfiguration.DefaultRememberMeRefreshTokenValidForNumberMinutes, 1);

            var accessCookieOptions = CreateCookieOptions(cookieDomain, accessExpiresUtc ?? DateTime.UtcNow.AddMinutes(accessLifetimeMinutes));
            var refreshCookieOptions = CreateCookieOptions(cookieDomain, refreshExpiresUtc ?? DateTime.UtcNow.AddMinutes(refreshLifetimeMinutes));

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                httpContext.Response.Cookies.Append($"{domain}", accessToken, accessCookieOptions);
            }

            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                httpContext.Response.Cookies.Append($"{IdpConstants.RefreshTokenCookieName}_{domain}", refreshToken, refreshCookieOptions);
            }
        }

        public async Task<IActionResult> GetLoginOptionsAsync()
        {
            var config = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            var identityProviders = await _authenticationRepository.GetIdentityProvidersAsync();

            // Filter to only social providers that are active
            var socialProviders = identityProviders
                .Where(p => p.IsActive && p.ProviderType == "social")
                .ToList();

            // Return only metadata, NOT authorization URLs
            var ssoInfo = config.AllowedGrantTypes.Contains("social") && socialProviders.Any()
                          ? socialProviders.Select(provider => new
                          {
                              provider = provider.Provider,
                              displayName = provider.DisplayName,
                              icon = provider.Provider // Can be URL or icon name
                          }).ToList()
                          : null;

            return new OkObjectResult(new
            {
                AllowedGrantTypes = config.AllowedGrantTypes.Except(["mfa_code", "refresh_token"]),
                SsoInfo = ssoInfo
            });
        }

        /// <summary>
        /// Initialize social provider authorization - generates state, stores in cache, returns authorization URL
        /// Standard OAuth 2.0 Authorization Code flow initialization
        /// </summary>
        public async Task<IActionResult> GetSocialAuthorizationUrlAsync(string provider, string redirectUri)
        {
            if (string.IsNullOrWhiteSpace(provider))
                return new BadRequestObjectResult(new { error = "provider_required", error_description = "Provider name is required" });

            try
            {
                // Get provider configuration
                var identityProvider = await _authenticationRepository.GetIdentityProviderAsync(provider);
                if (identityProvider == null)
                    return new NotFoundObjectResult(new { error = "provider_not_found", error_description = $"Provider '{provider}' not configured" });

                if (!identityProvider.IsActive)
                    return new BadRequestObjectResult(new { error = "provider_inactive", error_description = $"Provider '{provider}' is not active" });

                if (identityProvider.ProviderType != "social")
                    return new BadRequestObjectResult(new { error = "invalid_provider_type", error_description = $"Provider '{provider}' is not a social provider" });

                // Generate state for CSRF protection
                var state = Guid.NewGuid().ToString("n");

                // Store state in cache (5 minute TTL for authorization flow)
                var cacheKey = $"oidc_state:{state}";
                await _cacheClient.AddStringValueAsync(cacheKey, state, 300); // 5 minutes in seconds

                // Build authorization URL
                var scope = identityProvider.Scope ?? "openid profile email";
                redirectUri = string.IsNullOrWhiteSpace(redirectUri) ? identityProvider.RedirectUris.FirstOrDefault() : redirectUri;
                var authorizationUrl = BuildAuthorizationUrl(identityProvider, state, redirectUri, scope);

                return new OkObjectResult(new
                {
                    state,
                    authorizationUrl,
                    displayName = identityProvider.DisplayName,
                    provider,
                    requirePkce = identityProvider.RequirePkce == true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting social authorization URL for provider: {Provider}", provider);
                return new BadRequestObjectResult(new { error = "authorization_url_generation_failed", error_description = ex.Message });
            }
        }

        /// <summary>
        /// Initialize OIDC client authorization - generates state, stores in cache, returns authorization URL
        /// For internal OIDC clients (Service-A, Service-B, etc.)
        /// </summary>
        /// <summary>
        /// Initiate OIDC social provider authentication flow
        /// Called when user selects a provider from OIDC login page
        /// Generates state for social provider and redirects to social provider
        /// </summary>
        public async Task<IActionResult> GetOidcSocialAuthorizationUrlAsync(string provider, string oidcState, string redirectUri)
        {
            if (string.IsNullOrWhiteSpace(provider))
                return new BadRequestObjectResult(new { error = "provider_required", error_description = "Provider name is required" });

            if (string.IsNullOrWhiteSpace(oidcState))
                return new BadRequestObjectResult(new { error = "oidc_state_required", error_description = "OIDC state is required" });

            try
            {
                // Retrieve OIDC context from cache
                var contextKey = $"oidc_context:{oidcState}";
                var contextJson = await _cacheClient.GetStringValueAsync(contextKey);
                if (string.IsNullOrWhiteSpace(contextJson))
                    return new BadRequestObjectResult(new { error = "invalid_oidc_state", error_description = "OIDC flow expired or invalid" });

                // Get provider configuration
                var identityProvider = await _authenticationRepository.GetIdentityProviderAsync(provider);
                if (identityProvider == null)
                    return new NotFoundObjectResult(new { error = "provider_not_found", error_description = $"Provider '{provider}' not configured" });

                if (!identityProvider.IsActive)
                    return new BadRequestObjectResult(new { error = "provider_inactive", error_description = $"Provider '{provider}' is not active" });

                // Generate state for social provider (separate from OIDC state)
                var socialState = Guid.NewGuid().ToString("n");

                // Store state with reference to OIDC context
                // This links the social provider callback back to the OIDC flow
                var stateKey = $"oidc_social_state:{socialState}";
                var stateValue = System.Text.Json.JsonSerializer.Serialize(new
                {
                    oidcState,
                    provider,
                    createdAt = DateTime.UtcNow
                });
                await _cacheClient.AddStringValueAsync(stateKey, stateValue, 300); // 5 minute TTL

                // Build authorization URL for social provider
                // Callback should redirect to /auth/oidc/callback with provider and state
                var scope = identityProvider.Scope ?? "openid profile email";
                redirectUri = string.IsNullOrWhiteSpace(redirectUri) ? identityProvider.RedirectUris.FirstOrDefault() : redirectUri;
                var authorizationUrl = BuildAuthorizationUrl(identityProvider, socialState, redirectUri, scope);

                return new OkObjectResult(new
                {
                    socialState,
                    authorizationUrl,
                    provider,
                    oidcState,
                    requirePkce = identityProvider.RequirePkce == true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting OIDC social authorization URL for provider: {Provider}", provider);
                return new BadRequestObjectResult(new { error = "authorization_url_generation_failed", error_description = ex.Message });
            }
        }

        /// <summary>
        /// Build authorization URL with proper OAuth 2.0 parameters
        /// </summary>
        private string BuildAuthorizationUrl(IdentityProvider provider, string state, string redirectUri, string scope, string? codeChallenge = null)
        {
            try
            {
                // Standard OAuth 2.0 Authorization Code flow parameters
                var authUrl = provider.AuthorizationUrl;
                var separator = authUrl.Contains("?") ? "&" : "?";

                var authorizationUrl = $"{authUrl}{separator}" +
                    $"response_type=code&" +
                    $"client_id={Uri.EscapeDataString(provider.ClientId ?? "")}&" +
                    $"redirect_uri={Uri.EscapeDataString(redirectUri ?? "")}&" +
                    $"scope={Uri.EscapeDataString(scope)}&" +
                    $"state={Uri.EscapeDataString(state)}";

                // Add PKCE if required (code_challenge generated on frontend, passed from caller)
                if (provider.RequirePkce == true && !string.IsNullOrWhiteSpace(codeChallenge))
                {
                    authorizationUrl += $"&code_challenge={Uri.EscapeDataString(codeChallenge)}&code_challenge_method=S256";
                }

                return authorizationUrl;
            }
            catch
            {
                // Fallback to simple construction
                return $"{provider.AuthorizationUrl}?response_type=code&client_id={Uri.EscapeDataString(provider.ClientId ?? "")}&state={state}&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString(scope)}";
            }
        }

        public async Task<bool> TriggerBackchannelLogoutAllAsync(HttpRequest httpRequest)
        {
            var bc = BlocksContext.GetContext();
            var logoutEventId = Guid.NewGuid().ToString("n");
            var clients = await _authenticationRepository.GetOIDCCredentialsByTenantAsync();
            var backchannelUris = clients
                .Select(client => client.BackChannelLogoutUri)
                .Where(uri => !string.IsNullOrWhiteSpace(uri))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (backchannelUris.Count == 0)
            {
                _logger.LogInformation("No backchannel logout URIs configured for tenant {TenantId}", bc?.TenantId);
                return true;
            }

            var requestPayload = new
            {
                logout_event_id = logoutEventId,
                user_id = bc?.UserId,
                tenant_id = bc?.TenantId,
                event_name = "logout_all",
                session_id = httpRequest.Cookies["idp_session_id"],
                occurred_at_utc = DateTime.UtcNow
            };

            const int maxAttempts = 3;
            var allSucceeded = true;
            foreach (var uri in backchannelUris)
            {
                var delivered = false;
                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    await PersistBackchannelDeliveryAuditAsync(logoutEventId, uri!, "pending", attempt, null, "dispatching_backchannel_logout");

                    try
                    {
                        var response = await BackchannelHttpClient.PostAsJsonAsync(uri!, requestPayload);
                        if (response.IsSuccessStatusCode)
                        {
                            delivered = true;
                            await PersistBackchannelDeliveryAuditAsync(logoutEventId, uri!, "sent", attempt, (int)response.StatusCode, "backchannel_logout_delivered");
                            await PublishSecurityEventAsync(httpRequest, "backchannel_logout_succeeded", "dispatch_backchannel_logout", "success", null, logoutEventId, uri);
                            _logger.LogInformation(
                                "SecurityEvent backchannel_logout_succeeded event={LogoutEventId} tenant={TenantId} uri={Uri} attempt={Attempt}",
                                logoutEventId,
                                bc?.TenantId,
                                uri,
                                attempt);
                            break;
                        }

                        await PersistBackchannelDeliveryAuditAsync(logoutEventId, uri!, "failed", attempt, (int)response.StatusCode, "backchannel_logout_delivery_failed");
                        await PublishSecurityEventAsync(httpRequest, "backchannel_logout_failed", "dispatch_backchannel_logout", "failed", $"status_{(int)response.StatusCode}", logoutEventId, uri);
                        _logger.LogWarning(
                            "SecurityEvent backchannel_logout_failed event={LogoutEventId} tenant={TenantId} uri={Uri} status={StatusCode} attempt={Attempt}",
                            logoutEventId,
                            bc?.TenantId,
                            uri,
                            (int)response.StatusCode,
                            attempt);
                    }

                    catch (Exception ex)
                    {
                        await PersistBackchannelDeliveryAuditAsync(logoutEventId, uri!, "failed_exception", attempt, null, ex.GetType().Name);
                        await PublishSecurityEventAsync(httpRequest, "backchannel_logout_exception", "dispatch_backchannel_logout", "failed", ex.GetType().Name, logoutEventId, uri);
                        _logger.LogWarning(
                            ex,
                            "SecurityEvent backchannel_logout_exception event={LogoutEventId} tenant={TenantId} uri={Uri} attempt={Attempt}",
                            logoutEventId,
                            bc?.TenantId,
                            uri,
                            attempt);
                    }

                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt));
                    }
                }

                if (!delivered)
                {
                    allSucceeded = false;
                }
            }

            return allSucceeded;
        }

        private async Task PersistBackchannelDeliveryAuditAsync(string logoutEventId, string uri, string status, int attempt, int? statusCode, string details)
        {
            var bc = BlocksContext.GetContext();
            var log = new AuditLogModel
            {
                EventType = "backchannel_logout_delivery",
                UserId = bc?.UserId,
                TenantId = bc?.TenantId,
                Severity = status.StartsWith("failed", StringComparison.OrdinalIgnoreCase) ? "WARN" : "INFO",
                Status = status,
                Message = $"backchannel logout {status}",
                Details = $"event={logoutEventId};attempt={attempt};uri={uri};status_code={(statusCode?.ToString() ?? "n/a")};reason={details}",
                Timestamp = DateTime.UtcNow
            };

            await _auditLogRepository.CreateAsync(log);
        }

        private async Task PublishSecurityEventAsync(HttpRequest request, string eventName, string actionBy, string outcome, string? reasonCode, string correlationId, string? targetUri = null)
        {
            var bc = BlocksContext.GetContext();
            var timelineEvent = new UserAuthenticationTimelineEvent
            {
                DeviceInformation = _authenticationDomainService.GetDeviceInfo(request?.Headers?.UserAgent.ToString() ?? string.Empty),
                IpAddresses = string.Join(",", _authenticationDomainService.GetVisitorsIpAddresses(request?.HttpContext)),
                Event = eventName,
                ActionBy = actionBy,
                UserId = bc?.UserId,
                TenantId = bc?.TenantId,
                SessionId = request?.Cookies[IdpSessionCookieName],
                CorrelationId = correlationId,
                Outcome = outcome,
                ReasonCode = reasonCode,
                RiskLevel = "low",
                ClientId = targetUri
            };

            await _authenticationDomainService.SendToQueueAsync(IdpConstants.AuthenticationQueue, timelineEvent);
        }

        public async Task<ClaimsPrincipal?> GetPrincipalFromTokenAsync(HttpRequest request, string tenantId, bool IsUserInfoGetRequest = false)
        {
            var (token, _) = TokenHelper.GetToken(request, _tenants);
            var tenant = _tenants.GetTenantByID(tenantId);
            if (tenant == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(token))
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                string cacheKey = $"{Public_Cert_Cache_Prefix}{tenant.TenantId}";
                var certificateData = await _cacheClient.CacheDatabase().StringGetAsync(cacheKey);
                var validationParams = tenant.JwtTokenParameters;
                var publicCert = X509CertificateLoader.LoadPkcs12(certificateData, validationParams.PublicCertificatePassword);
                var tokenValidationParameters = !IsUserInfoGetRequest ? new TokenValidationParameters { ValidateLifetime = true, ClockSkew = TimeSpan.Zero, IssuerSigningKey = new X509SecurityKey(publicCert), ValidateIssuerSigningKey = true, ValidateIssuer = true, ValidIssuer = validationParams?.Issuer, ValidAudience = DomainResolver.GetAudience(tenant), ValidateAudience = true, SaveSigninToken = true } :
                                                                      new TokenValidationParameters { ValidateLifetime = true, ClockSkew = TimeSpan.Zero, IssuerSigningKey = new X509SecurityKey(publicCert), ValidateIssuerSigningKey = true, ValidateIssuer = false, ValidateAudience = false, SaveSigninToken = true };
                return tokenHandler.ValidateToken(token, tokenValidationParameters, out _);
            }

            return null;
        }

        public (bool IsValid, Dictionary<string, object> UserInfo) BuildOidcUserInfo(ClaimsPrincipal principal)
        {
            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(BlocksContext.USER_ID_CLAIM)?.Value;

            if (string.IsNullOrWhiteSpace(sub))
            {
                return (false, new Dictionary<string, object>());
            }

            var userInfo = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = sub
            };

            var grantedScopes = GetGrantedScopes(principal);

            if (HasScope(grantedScopes, "profile"))
            {
                AddSingleClaimIfPresent(principal, userInfo, "name", BlocksContext.DISPLAY_NAME_CLAIM);
                AddSingleClaimIfPresent(principal, userInfo, "preferred_username", BlocksContext.USER_NAME_CLAIM);
            }

            if (HasScope(grantedScopes, "email"))
            {
                AddSingleClaimIfPresent(principal, userInfo, "email", BlocksContext.EMAIL_CLAIM);
            }

            if (HasScope(grantedScopes, "phone"))
            {
                AddSingleClaimIfPresent(principal, userInfo, "phone_number", BlocksContext.PHONE_NUMBER_CLAIM);
            }

            AddSingleClaimIfPresent(principal, userInfo, BlocksContext.TENANT_ID_CLAIM, BlocksContext.TENANT_ID_CLAIM);
            AddSingleClaimIfPresent(principal, userInfo, BlocksContext.ORGANIZATION_ID_CLAIM, BlocksContext.ORGANIZATION_ID_CLAIM);

            AddArrayClaimIfPresent(principal, userInfo, BlocksContext.ROLES_CLAIM, BlocksContext.ROLES_CLAIM);
            AddArrayClaimIfPresent(principal, userInfo, BlocksContext.PERMISSION_CLAIM, BlocksContext.PERMISSION_CLAIM);
            AddArrayClaimIfPresent(principal, userInfo, BlocksContext.SERVICE_ACCESS_CLAIM, BlocksContext.SERVICE_ACCESS_CLAIM);

            return (true, userInfo);
        }

        private static HashSet<string> GetGrantedScopes(ClaimsPrincipal principal)
        {
            var scopeValues = principal.FindAll("scope")
                .Concat(principal.FindAll("scp"))
                .Select(claim => claim.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value));

            return scopeValues
                .SelectMany(value => value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static bool HasScope(HashSet<string> grantedScopes, string scope)
        {
            return grantedScopes.Count == 0 || grantedScopes.Contains(scope);
        }

        private static void AddSingleClaimIfPresent(ClaimsPrincipal principal, Dictionary<string, object> userInfo, string outputClaimName, string sourceClaimType)
        {
            var value = principal.FindFirst(sourceClaimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                userInfo[outputClaimName] = value;
            }
        }

        private static void AddArrayClaimIfPresent(ClaimsPrincipal principal, Dictionary<string, object> userInfo, string outputClaimName, string sourceClaimType)
        {
            var values = principal.FindAll(sourceClaimType)
                .Select(claim => claim.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (values.Length > 0)
            {
                userInfo[outputClaimName] = values;
            }
        }

        private async Task<IActionResult> BuildTokenResponseAsync(TokenResponse response, HttpContext httpContext)
        {
            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                var statusCode = response.StatusCode > 0 ? response.StatusCode : StatusCodes.Status400BadRequest;
                return new ObjectResult(new
                {
                    error = response.Error,
                    error_description = response.ErrorDescription,
                    redirect_url = response.SsoUserRedirectUrl
                })
                {
                    StatusCode = statusCode
                };
            }

            var clientId = TryGetClientIdFromAccessToken(response.AccessToken);
            var useTokensCookie = await ResolveUseTokensCookieAsync(clientId);

            var cookiesSet = false;
            if (useTokensCookie)
            {
                cookiesSet = AppendCookies(response, httpContext);
            }

            await EnsureIdpSessionForLoginAsync(response, httpContext);

            if (cookiesSet)
            {
                return new OkObjectResult(new
                {
                    token_type = response.TokenType,
                    expires_in = response.ExpiresIn,
                    scope = response.Scope,
                    id_token = response.IdToken
                });
            }

            return new OkObjectResult(new
            {
                access_token = response.AccessToken,
                refresh_token = response.RefreshToken,
                token_type = response.TokenType,
                expires_in = response.ExpiresIn,
                expires_utc = response.ExpiresUtc,
                refresh_expires_utc = response.RefreshExpiresUtc,
                scope = response.Scope,
                id_token = response.IdToken
            });
        }

        private async Task<bool> ResolveUseTokensCookieAsync(string? clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return true;
            }

            var registration = await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
            return registration?.UseTokensCookie ?? true;
        }

        private static string? TryGetClientIdFromAccessToken(string? accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return null;
            }

            try
            {
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
                return jwt.Claims.FirstOrDefault(c => c.Type == "client_id")?.Value
                    ?? jwt.Claims.FirstOrDefault(c => c.Type == "azp")?.Value;
            }
            catch
            {
                return null;
            }
        }

        private bool AppendCookies(TokenResponse response, HttpContext httpContext)
        {
            // Validate response has no error indicator
            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                return false; // Cannot set cookies for error responses
            }

            // Validate access token is present
            if (string.IsNullOrWhiteSpace(response.AccessToken))
            {
                return false; // Cannot set cookies without valid access token
            }

            var bc = BlocksContext.GetContext();
            var tenant = _tenants.GetTenantByID(bc?.TenantId ?? "default");
            var (domain, _, isResolved) = DomainResolver.ResolveDomain(tenant, httpContext.Request);
            if (!isResolved || string.IsNullOrWhiteSpace(domain))
            {
                return false;
            }

            var accessCookieOptions = CreateCookieOptions(response.CookieDomain, response.ExpiresUtc);
            var refreshCookieOptions = CreateCookieOptions(response.CookieDomain, response.RefreshExpiresUtc);

            httpContext.Response.Cookies.Append($"{domain}", response.AccessToken, accessCookieOptions);

            if (!string.IsNullOrWhiteSpace(response.RefreshToken))
            {
                httpContext.Response.Cookies.Append($"{IdpConstants.RefreshTokenCookieName}_{domain}", response.RefreshToken, refreshCookieOptions);
            }

            return true;
        }

        /// <summary>
        /// Conditionally append tokens to cookies or return them in a response object
        /// based on client configuration (UseTokensCookie property from OidcClientRegistration)
        /// </summary>
        public Task<object> HandleTokenResponseConditionallyAsync(
            TokenResponse response,
            HttpResponse httpResponse,
            bool useTokensCookie,
            string? clientId = null)
        {
            // If response has an error, return error response immediately
            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                return Task.FromResult<object>(new
                {
                    error = response.Error,
                    error_description = response.ErrorDescription,
                    tenant_id = BlocksContext.GetContext()?.TenantId ?? "default",
                    client_id = clientId,
                    status_code = response.StatusCode
                });
            }

            var cookiesSet = false;
            if (useTokensCookie)
            {
                cookiesSet = AppendCookies(response, httpResponse.HttpContext);
            }

            if (cookiesSet)
            {
                return Task.FromResult<object>(new
                {
                    token_type = response.TokenType ?? "Bearer",
                    expires_in = (response.ExpiresUtc - DateTime.UtcNow).TotalSeconds,
                    scope = response.Scope,
                    tenant_id = BlocksContext.GetContext()?.TenantId ?? "default",
                    client_id = clientId,
                    cookie_set = true
                });
            }

            // Fallback: return tokens in response body if cookies couldn't be set
            return Task.FromResult<object>(new
            {
                access_token = response.AccessToken,
                token_type = response.TokenType ?? "Bearer",
                expires_in = (response.ExpiresUtc - DateTime.UtcNow).TotalSeconds,
                refresh_token = response.RefreshToken,
                id_token = response.IdToken,
                scope = response.Scope,
                tenant_id = BlocksContext.GetContext()?.TenantId ?? "default",
                client_id = clientId,
                cookie_set = false
            });
        }

        private static CookieOptions CreateCookieOptions(string? domain, DateTime expiresUtc)
        {
            var isLocal = DomainResolver.IsLocalhost();
            var cookieDomain = isLocal ? null : (string.IsNullOrWhiteSpace(domain) ? null : domain);
            return new CookieOptions
            {
                Domain = cookieDomain,
                HttpOnly = true,
                Secure = !isLocal,
                SameSite = isLocal ? SameSiteMode.None : SameSiteMode.Strict,
                Path = "/",
                Expires = expiresUtc == default ? DateTime.UtcNow : expiresUtc
            };
        }



        private async Task EnsureIdpSessionForLoginAsync(TokenResponse tokenResponse, HttpContext httpContext)
        {
            if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                return;
            }

            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(tokenResponse.AccessToken);
            var userId = jwt.Claims.FirstOrDefault(c => c.Type == BlocksContext.USER_ID_CLAIM)?.Value;
            var tenantId = jwt.Claims.FirstOrDefault(c => c.Type == BlocksContext.TENANT_ID_CLAIM)?.Value;

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tenantId))
            {
                return;
            }

            var sessionId = httpContext.Request.Cookies[IdpSessionCookieName];
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = await _idpSessionService.CreateSessionAsync(userId, tenantId, httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            }
            else
            {
                var existingSession = await _idpSessionService.GetSessionAsync(sessionId);
                if (existingSession == null || existingSession.RevokedAt.HasValue || existingSession.IsExpired())
                {
                    sessionId = await _idpSessionService.CreateSessionAsync(userId, tenantId, httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                }
                else
                {
                    var accountExists = existingSession.Accounts.Any(a =>
                        string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));

                    if (!accountExists)
                    {
                        await _idpSessionService.AddAccountAsync(sessionId, userId, tenantId, userId);
                    }
                    else
                    {
                        await _idpSessionService.UpdateActivityAsync(sessionId);
                    }

                    // Rotate session id on successful login transition to reduce fixation risk.
                    sessionId = await _idpSessionService.RotateSessionAsync(sessionId, "login_success") ?? sessionId;
                }
            }

            httpContext.Response.Cookies.Append(IdpSessionCookieName, sessionId, CreateCookieOptions(tokenResponse.CookieDomain, tokenResponse.RefreshExpiresUtc));
        }

        public async Task<bool> EnsureIdpSessionForOidcCallbackAsync(HttpContext httpContext, string userId, string tenantId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tenantId))
            {
                _logger.LogWarning("Cannot ensure IdP session: userId or tenantId is empty");
                return false;
            }

            try
            {
                var sessionId = httpContext.Request.Cookies[IdpSessionCookieName];
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    // Create new session for this OIDC callback
                    sessionId = await _idpSessionService.CreateSessionAsync(userId, tenantId, httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                }
                else
                {
                    // Validate and potentially update existing session
                    var existingSession = await _idpSessionService.GetSessionAsync(sessionId);
                    if (existingSession == null || existingSession.RevokedAt.HasValue || existingSession.IsExpired())
                    {
                        // Create new session if existing one is invalid
                        sessionId = await _idpSessionService.CreateSessionAsync(userId, tenantId, httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                    }
                    else
                    {
                        // Add account to existing session if not present
                        var accountExists = existingSession.Accounts.Any(a =>
                            string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));

                        if (!accountExists)
                        {
                            await _idpSessionService.AddAccountAsync(sessionId, userId, tenantId, userId);
                        }
                        else
                        {
                            await _idpSessionService.UpdateActivityAsync(sessionId);
                        }

                        // Rotate session on callback for security
                        sessionId = await _idpSessionService.RotateSessionAsync(sessionId, "oidc_callback") ?? sessionId;
                    }
                }

                // Set session cookie
                var domain = DomainResolver.ResolveDomain(_tenants.GetTenantByID(tenantId), httpContext.Request).domain;
                httpContext.Response.Cookies.Append(IdpSessionCookieName, sessionId, CreateCookieOptions(null, DateTime.UtcNow.AddDays(30)));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring IdP session for OIDC callback: userId={UserId}, tenantId={TenantId}", userId, tenantId);
                return false;
            }
        }

        public async Task<BaseResponse> CreateIdentityProviderAsync(IdentityProvider provider)
        {
            return await _authenticationDomainService.CreateIdentityProviderAsync(provider);
        }

        public async Task<IdentityProvider?> GetIdentityProviderAsync(string provider)
        {
            return await _authenticationDomainService.GetIdentityProviderAsync(provider);
        }

        public async Task<IdentityProvider?> GetIdentityProviderByIdAsync(string id)
        {
            return await _authenticationDomainService.GetIdentityProviderByIdAsync(id);
        }

        public async Task<List<IdentityProvider>> GetAllIdentityProvidersAsync()
        {
            return await _authenticationDomainService.GetAllIdentityProvidersAsync();
        }

        public async Task<BaseResponse> UpdateIdentityProviderAsync(IdentityProvider provider)
        {
            return await _authenticationDomainService.UpdateIdentityProviderAsync(provider);
        }

        public async Task<BaseResponse> DeleteIdentityProviderAsync(string id)
        {
            return await _authenticationDomainService.DeleteIdentityProviderAsync(id);
        }

        public async Task<BaseResponse> UpdateIdentityProviderStatusAsync(string id, bool isActive)
        {
            return await _authenticationDomainService.UpdateIdentityProviderStatusAsync(id, isActive);
        }
    }
}
