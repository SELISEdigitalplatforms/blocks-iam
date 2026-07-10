using Authentication.DomainService.Dtos;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.Dtos;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using Iam.DomainService.Utilities;
using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Driver;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Authentication.DomainService.Authentication
{
    public sealed class AuthenticationService : IAuthenticationService
    {
        private static readonly HttpClient BackchannelHttpClient = new();
        private readonly ILogger<AuthenticationService> _logger;
        private readonly ICacheClient _cacheClient;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly IAuthSessionFacade _authSession;
        private readonly ITenants _tenants;

        private const string PublicCertCachePrefix = "tetocertpublic::";

        public AuthenticationService(
            ILogger<AuthenticationService> logger,
            ICacheClient cacheClient,
            IAuthenticationRepository authenticationRepository,
            IAuditLogRepository auditLogRepository,
            IAuthenticationDomainService authenticationDomainService,
            IAuthSessionFacade authSession,
            ITenants tenants
        )
        {
            _logger = logger;
            _cacheClient = cacheClient;
            _authenticationRepository = authenticationRepository;
            _auditLogRepository = auditLogRepository;
            _authenticationDomainService = authenticationDomainService;
            _authSession = authSession;
            _tenants = tenants;
        }

        public async Task<IActionResult> BuildFlowResultAsync(AuthenticationFlowResult result, HttpContext httpContext)
        {
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                return new ObjectResult(new
                {
                    error = result.Error,
                    error_description = result.ErrorDescription,
                    captcha_required = result.CaptchaRequired || string.Equals(result.Error, OAuthError.CaptchaEnabled, StringComparison.OrdinalIgnoreCase),
                    captcha_site_key = result.CaptchaSiteKey
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
            var bc = BlocksContext.GetContext();
            var sessionId = httpContext.Request.Cookies[IdpConstants.BuildIdpSessionCookieKey(bc!.TenantId)];
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return true;
            }

            if (isGlobalLogout)
            {
                await _authSession.RevokeSessionAsync(sessionId, "logout_all");
                return true;
            }

            var userId = user.FindFirst(bc?.UserId)?.Value ?? user.FindFirst("user_id")?.Value;

            if (string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            await _authSession.RemoveAccountAsync(sessionId, userId, bc.TenantId);
            var session = await _authSession.GetSessionAsync(sessionId);

            if (session == null || session.RevokedAt.HasValue || session.IsExpired())
            {
                return true;
            }

            return session.Accounts.Count == 0;
        }

        public void ClearIdpSessionCookie(HttpResponse response)
        {
            var bc = BlocksContext.GetContext();
            response.Cookies.Delete(IdpConstants.BuildIdpSessionCookieKey(bc!.TenantId));
        }

        public async Task<LogoutResponse> LogoutUser(string refreshToken, HttpRequest httpRequest)
        {
            _logger.LogInformation("Logout process start");

            var result = await ProcessLogout(refreshToken, httpRequest);

            await ProcessTimeline(httpRequest, false);
            return new LogoutResponse
            {
                IsSuccess = result,
            };

        }

        public async Task<bool> ProcessLogout(string refreshToken, HttpRequest httpRequest)
        {
            var bc = BlocksContext.GetContext();

            var refreshTokenCache = await _cacheClient.GetStringValueAsync(refreshToken);
            if (string.IsNullOrWhiteSpace(refreshTokenCache))
            {
                _logger.LogWarning("No active session found for refresh token during logout");
                return false;
            }

            var refreshTokenSession = new RefreshTokenCache();

            try
            {
                refreshTokenSession = JsonSerializer.Deserialize<RefreshTokenCache>(refreshTokenCache);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during refresh-token revocation in logout");
                return false;
            }

            // Revoke the refresh token (marks IsRevoked in IdpRefreshTokens and syncs identity session status).
            var revokeResult = await _authSession.RevokeTokenAsync(refreshToken, GrantTypes.RefreshToken, refreshTokenSession?.ClientId);
            if (!revokeResult.Success)
            {
                _logger.LogWarning("Refresh-token revocation failed during logout: {Error}", revokeResult.Error ?? "unknown_error");
            }

            await _cacheClient.RemoveKeyAsync(refreshToken);

            var result = await _authenticationRepository.RevokeIdentitySessionAsync(refreshToken, bc?.UserId ?? "");

            return result;
        }

        public async Task<LogoutResponse> LogoutAll(HttpRequest httpRequest)
        {
            var bc = BlocksContext.GetContext();

            var refreshTokens = (await _authenticationRepository.GetActiveIdentitySessionByUserIdAsync(bc?.UserId ?? string.Empty)).Select(x => x.RefreshToken).ToList();

            var revokeTasks = refreshTokens.Select(async token =>
            {
                var result = await _authSession.RevokeTokenAsync(token, GrantTypes.RefreshToken, string.Empty);
                if (!result.Success)
                {
                    _logger.LogWarning("Refresh-token revocation failed during logout-all: {Error}", result.Error ?? "unknown_error");
                }
                await _cacheClient.RemoveKeyAsync(token);
            });
            await Task.WhenAll(revokeTasks);

            var result = await _authenticationRepository.RevokeIdentitySessionsByRefreshTokensAsync(refreshTokens);

            await ProcessTimeline(httpRequest, true);
            return new LogoutResponse
            {
                IsSuccess = result,
            };
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
            var (domain, cookieDomain, _) = DomainResolver.ResolveDomain(tenant, request);
            var cookieOptions = CreateCookieOptions(cookieDomain, DateTime.UtcNow.AddDays(-1));

            request.HttpContext.Response.Cookies.Delete($"{IdpConstants.RefreshTokenCookieName}_{domain}", cookieOptions);
            request.HttpContext.Response.Cookies.Delete($"{domain}", cookieOptions);
            // request.HttpContext.Response.Cookies.Delete(IdpConstants.ImpersonationIdCookieName, cookieOptions);
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
            var accessLifetimeMinutes = Math.Max(authConfiguration?.AccessTokenValidForNumberMinutes ?? IdentityConfiguration.DefaultAccessTokenValidForNumberMinutes, 1);
            var refreshLifetimeMinutes = Math.Max(authConfiguration?.AbsoluteRefreshTokenValidForNumberMinutes ?? IdentityConfiguration.DefaultRememberMeRefreshTokenValidForNumberMinutes, 1);

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
            var identityProviders = await _authenticationRepository.GetIdentityProvidersAsync();

            // Filter to only social providers that are active
            var socialProviders = identityProviders
                .Where(p => p.IsActive && p.ProviderType == "social")
                .ToList();

            // Return only metadata, NOT authorization URLs
            var ssoInfo = socialProviders.Any()
                          ? socialProviders.Select(provider => new
                          {
                              provider = provider.Provider,
                              displayName = provider.DisplayName,
                              icon = provider.Provider, // Can be URL or icon name
                              redirectUris = provider.RedirectUris,
                              clientId = provider.ClientId,
                          }).ToList()
                          : null;


            var gts = new List<string> {
                GrantTypes.AuthCode,
                GrantTypes.ClientCredential,
                GrantTypes.RefreshToken
            };

            return new OkObjectResult(new
            {
                AllowedGrantTypes = gts,
                SsoInfo = ssoInfo
            });
        }

        /// <summary>
        /// Initialize social provider authorization - generates state, stores in cache, returns authorization URL
        /// Standard OAuth 2.0 Authorization Code flow initialization
        /// </summary>
        public async Task<IActionResult> GetSocialAuthorizationUrlAsync(string clientId, string redirectUri)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                return new BadRequestObjectResult(new { error = "client_id_required", error_description = "Client ID is required" });

            try
            {
                // Get provider configuration
                var identityProvider = await _authenticationRepository.GetIdentityProviderByClientIdAsync(clientId);
                if (identityProvider == null)
                    return new NotFoundObjectResult(new { error = "provider_not_found", error_description = $"Provider '{clientId}' not configured" });

                if (!identityProvider.IsActive)
                    return new BadRequestObjectResult(new { error = "provider_inactive", error_description = $"Provider '{clientId}' is not active" });

                if (identityProvider.ProviderType != "social")
                    return new BadRequestObjectResult(new { error = "invalid_provider_type", error_description = $"Provider '{clientId}' is not a social provider" });

                // Generate state for CSRF protection
                var state = Guid.NewGuid().ToString("n");
                var stateValue = JsonSerializer.Serialize(new StateInfo
                {
                    ClientId = clientId,
                    Provider = identityProvider.Provider,
                    State = state,
                    RedirectUri = redirectUri,
                    Audience = clientId,
                });
                await _cacheClient.AddStringValueAsync(state, stateValue, IdpConstants.OidcStateCacheTtlSeconds); // 5 minutes in seconds

                // Build authorization URL
                var scope = identityProvider.Scope ?? IdpConstants.OpenIdProfileEmailScope;
                redirectUri = string.IsNullOrWhiteSpace(redirectUri) ? identityProvider.RedirectUris.FirstOrDefault() : redirectUri;
                var authorizationUrl = BuildAuthorizationUrl(identityProvider, state, redirectUri, scope);

                return new OkObjectResult(new
                {
                    authorizationUrl,
                    requirePkce = identityProvider.RequirePkce == true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting social authorization URL for provider: {Provider}", clientId);
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
        public async Task<IActionResult> GetOidcSocialAuthorizationUrlAsync(string providerClientId, string oidcState, string providerRedirectUri)
        {
            if (string.IsNullOrWhiteSpace(providerClientId))
                return new BadRequestObjectResult(new { error = "client_id_required", error_description = "Client ID is required" });

            if (string.IsNullOrWhiteSpace(oidcState))
                return new BadRequestObjectResult(new { error = OAuthError.OidcStateRequired, error_description = "OIDC state is required" });

            try
            {
                // Retrieve OIDC context from cache
                var contextKey = $"oidc_context:{oidcState}";
                var contextJson = await _cacheClient.GetStringValueAsync(contextKey);
                if (string.IsNullOrWhiteSpace(contextJson))
                    return new BadRequestObjectResult(new { error = "invalid_oidc_state", error_description = "OIDC flow expired or invalid" });

                // Get provider configuration
                var identityProvider = await _authenticationRepository.GetIdentityProviderByClientIdAsync(providerClientId);
                if (identityProvider == null)
                    return new NotFoundObjectResult(new { error = "provider_not_found", error_description = $"Provider with client ID '{providerClientId}' not configured" });

                if (!identityProvider.IsActive)
                    return new BadRequestObjectResult(new { error = "provider_inactive", error_description = $"Provider with client ID '{providerClientId}' is not active" });

                // Generate state for social provider (separate from OIDC state)
                var socialState = Guid.NewGuid().ToString("n");

                // Store state with reference to OIDC context
                // This links the social provider callback back to the OIDC flow
                var stateKey = $"oidc_social_state:{socialState}";
                var stateValue = JsonSerializer.Serialize(new OidcSocialStateContext
                {
                    OidcState = oidcState,
                    Provider = identityProvider.Provider,
                    CreatedAt = DateTime.UtcNow
                });
                await _cacheClient.AddStringValueAsync(stateKey, stateValue, IdpConstants.OidcStateCacheTtlSeconds); // 5 minute TTL

                // Build authorization URL for social provider
                // Callback should redirect to /oidc/callback with provider and state
                var scope = identityProvider.Scope ?? IdpConstants.OpenIdProfileEmailScope;
                providerRedirectUri = string.IsNullOrWhiteSpace(providerRedirectUri) ? identityProvider.RedirectUris.FirstOrDefault() : providerRedirectUri;
                var authorizationUrl = BuildAuthorizationUrl(identityProvider, socialState, providerRedirectUri, scope);

                return new OkObjectResult(new
                {
                    authorizationUrl,
                    requirePkce = identityProvider.RequirePkce == true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting OIDC social authorization URL for provider: {ClientId}", providerClientId);
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
                    $"state={Uri.EscapeDataString(state)}&" +
                    $"tenant_id={Uri.EscapeDataString(BlocksContext.GetContext().TenantId)}";

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
                session_id = httpRequest.Cookies[IdpConstants.BuildIdpSessionCookieKey(bc?.TenantId)],
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
                            await PersistBackchannelDeliveryAuditAsync(logoutEventId, uri!, IdpConstants.StatusSent, attempt, (int)response.StatusCode, BackchannelAuditEvents.Delivered);
                            await PublishSecurityEventAsync(httpRequest, BackchannelAuditEvents.Succeeded, BackchannelAuditEvents.Dispatch, IdpConstants.StatusSuccess, null, logoutEventId, uri);
                            _logger.LogInformation(
                                "SecurityEvent backchannel_logout_succeeded event={LogoutEventId} tenant={TenantId} uri={Uri} attempt={Attempt}",
                                logoutEventId,
                                bc?.TenantId,
                                uri,
                                attempt);
                            break;
                        }

                            await PersistBackchannelDeliveryAuditAsync(logoutEventId, uri!, IdpConstants.StatusFailure, attempt, (int)response.StatusCode, BackchannelAuditEvents.DeliveryFailed);
                            await PublishSecurityEventAsync(httpRequest, BackchannelAuditEvents.Failed, BackchannelAuditEvents.Dispatch, IdpConstants.StatusFailure, $"status_{(int)response.StatusCode}", logoutEventId, uri);
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
                        await PublishSecurityEventAsync(httpRequest, BackchannelAuditEvents.Exception, BackchannelAuditEvents.Dispatch, IdpConstants.StatusFailure, ex.GetType().Name, logoutEventId, uri);
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
                        await Task.Delay(TimeSpan.FromMilliseconds(IdpConstants.BackchannelRetryBackoffMilliseconds * attempt));
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
                EventType = BackchannelAuditEvents.Delivery,
                UserId = bc?.UserId,
                TenantId = bc?.TenantId,
                Severity = status.StartsWith("failed", StringComparison.OrdinalIgnoreCase) ? IdpConstants.SeverityWarn : IdpConstants.SeverityInfo,
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
                SessionId = request?.Cookies[IdpConstants.BuildIdpSessionCookieKey(bc?.TenantId)],
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
                string cacheKey = $"{PublicCertCachePrefix}{tenant.TenantId}";
                var certificateData = await _cacheClient.CacheDatabase().StringGetAsync(cacheKey);
                var validationParams = tenant.JwtTokenParameters;
                var publicCert = X509CertificateLoader.LoadPkcs12(certificateData, validationParams.PublicCertificatePassword);
                var tokenValidationParameters = !IsUserInfoGetRequest ? new TokenValidationParameters { ValidateLifetime = true, ClockSkew = TimeSpan.Zero, IssuerSigningKey = new X509SecurityKey(publicCert), ValidateIssuerSigningKey = true, ValidateIssuer = true, ValidIssuer = validationParams?.Issuer, ValidAudience = DomainResolver.GetAudience(tenant), ValidateAudience = true, SaveSigninToken = true } :
                                                                      new TokenValidationParameters { ValidateLifetime = true, ClockSkew = TimeSpan.Zero, IssuerSigningKey = new X509SecurityKey(publicCert), ValidateIssuerSigningKey = true, ValidateIssuer = false, ValidateAudience = false, SaveSigninToken = true };
                return tokenHandler.ValidateToken(token, tokenValidationParameters, out _);
            }

            return null;
        }

        public async Task<(bool IsValid, Dictionary<string, object> UserInfo)> BuildOidcUserInfoAsync(ClaimsPrincipal principal)
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
            var user = await _authenticationRepository.GetUserByIdAsync(sub);

            if (HasScope(grantedScopes, "profile"))
            {
                var fullName = string.Join(' ', new[] { user?.FirstName, user?.LastName }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
                if (!string.IsNullOrWhiteSpace(fullName))
                {
                    userInfo["name"] = fullName;
                }
                if (!string.IsNullOrWhiteSpace(user?.UserName))
                {
                    userInfo["preferred_username"] = user.UserName;
                }
            }

            if (HasScope(grantedScopes, "email") && !string.IsNullOrWhiteSpace(user?.Email))
            {
                userInfo["email"] = user.Email;
            }

            if (HasScope(grantedScopes, "phone"))
            {
                AddSingleClaimIfPresent(principal, userInfo, "phone_number", BlocksContext.PHONE_NUMBER_CLAIM);
            }

            AddSingleClaimIfPresent(principal, userInfo, BlocksContext.TENANT_ID_CLAIM, BlocksContext.TENANT_ID_CLAIM);
            AddSingleClaimIfPresent(principal, userInfo, BlocksContext.ORGANIZATION_ID_CLAIM, BlocksContext.ORGANIZATION_ID_CLAIM);

            AddArrayClaimIfPresent(principal, userInfo, BlocksContext.ROLES_CLAIM, BlocksContext.ROLES_CLAIM);
            AddArrayClaimIfPresent(principal, userInfo, BlocksContext.PERMISSION_CLAIM, BlocksContext.PERMISSION_CLAIM);

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
            return DomainResolver.IsLocalhost()
                ? DomainResolver.CreateLoopbackCookieOptions(domain, expiresUtc)
                : DomainResolver.CreateProductionCookieOptions(domain, expiresUtc);
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
            var bc = BlocksContext.GetContext();
            var sessionId = httpContext.Request.Cookies[IdpConstants.BuildIdpSessionCookieKey(bc?.TenantId)];
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = await _authSession.CreateSessionAsync(userId, tenantId, httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            }
            else
            {
                var existingSession = await _authSession.GetSessionAsync(sessionId);
                if (existingSession == null || existingSession.RevokedAt.HasValue || existingSession.IsExpired())
                {
                    sessionId = await _authSession.CreateSessionAsync(userId, tenantId, httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                }
                else
                {
                    var accountExists = existingSession.Accounts.Any(a =>
                        string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));

                    if (!accountExists)
                    {
                        await _authSession.AddAccountAsync(sessionId, userId, tenantId, userId);
                    }
                    else
                    {
                        await _authSession.UpdateActivityAsync(sessionId);
                    }

                    // Rotate session id on successful login transition to reduce fixation risk.
                    sessionId = await _authSession.RotateSessionAsync(sessionId, LoginAuditEvents.LoginSuccess) ?? sessionId;
                }
            }

            httpContext.Response.Cookies.Append(IdpConstants.BuildIdpSessionCookieKey(bc?.TenantId), sessionId, CreateCookieOptions(tokenResponse.CookieDomain, tokenResponse.RefreshExpiresUtc));
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
                var bc = BlocksContext.GetContext();
                var sessionId = httpContext.Request.Cookies[IdpConstants.BuildIdpSessionCookieKey(bc?.TenantId)];
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    // Create new session for this OIDC callback
                    sessionId = await _authSession.CreateSessionAsync(userId, tenantId, httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                }
                else
                {
                    // Validate and potentially update existing session
                    var existingSession = await _authSession.GetSessionAsync(sessionId);
                    if (existingSession == null || existingSession.RevokedAt.HasValue || existingSession.IsExpired())
                    {
                        // Create new session if existing one is invalid
                        sessionId = await _authSession.CreateSessionAsync(userId, tenantId, httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                    }
                    else
                    {
                        // Add account to existing session if not present
                        var accountExists = existingSession.Accounts.Any(a =>
                            string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));

                        if (!accountExists)
                        {
                            await _authSession.AddAccountAsync(sessionId, userId, tenantId, userId);
                        }
                        else
                        {
                            await _authSession.UpdateActivityAsync(sessionId);
                        }

                        // Rotate session on callback for security
                        sessionId = await _authSession.RotateSessionAsync(sessionId, LoginAuditEvents.OidcCallback) ?? sessionId;
                    }
                }

                // Set session cookie
                var domain = DomainResolver.ResolveDomain(_tenants.GetTenantByID(tenantId), httpContext.Request).domain;
                httpContext.Response.Cookies.Append(IdpConstants.BuildIdpSessionCookieKey(bc?.TenantId), sessionId, CreateCookieOptions(null, DateTime.UtcNow.AddDays(IdpConstants.IdpSessionCookieTtlDays)));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring IdP session for OIDC callback: userId={UserId}, tenantId={TenantId}", userId, tenantId);
                return false;
            }
        }

        public async Task<BaseResponse> CreateIdentityProviderAsync(SaveIdentityProviderRequest request)
        {
            return await _authenticationDomainService.CreateIdentityProviderAsync(request);
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

        public async Task<BaseResponse> UpdateIdentityProviderAsync(string id, UpdateIdentityProviderRequest request)
        {
            return await _authenticationDomainService.UpdateIdentityProviderAsync(id, request);
        }

        public async Task<BaseResponse> DeleteIdentityProviderAsync(string id)
        {
            return await _authenticationDomainService.DeleteIdentityProviderAsync(id);
        }

        public async Task<BaseResponse> UpdateIdentityProviderStatusAsync(string id, bool isActive)
        {
            return await _authenticationDomainService.UpdateIdentityProviderStatusAsync(id, isActive);
        }

        public async Task<RotateOidcClientSecretResponse> RotateOidcClientSecretAsync(string itemId)
        {
            return await _authenticationDomainService.RotateOidcClientSecretAsync(itemId);
        }
        private async Task<bool> IsTenantSharedWithUserAsync(string userId, string targetTenantId)
        {
            try
            {
                var collection = _authenticationRepository.GetCollectionByName<BsonDocument>("ProjectPeoples");
                var filter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("UserId", userId),
                    Builders<BsonDocument>.Filter.Eq("TenantId", targetTenantId)
                );

                return await collection.Find(filter).Limit(1).AnyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate ProjectPeople share for user {UserId} and tenant {TenantId}", userId, targetTenantId);
                return false;
            }
        }

        private async Task<bool> HasOidcClientConfigurationAsync(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return false;
            }

            var oidcClient = await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
            return oidcClient != null;
        }

        public async Task<IActionResult> ExecuteImpersonateAsync(ImpersonateRequest request, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            var bc = BlocksContext.GetContext();
            var rootTenant = _tenants.GetTenantByID(bc!.TenantId);

            var (validation, userId, user) = await ValidateImpersonationRequestAsync(request, rootTenant, httpRequest);
            if (validation != null)
            {
                return validation;
            }

            var (rootDomain, rootCookieDomain, _) = DomainResolver.ResolveDomain(rootTenant, httpRequest);
            var rootRefreshToken = string.IsNullOrWhiteSpace(request.RefreshToken)
                ? CookieToken(httpRequest)
                : request.RefreshToken;

            if (string.IsNullOrWhiteSpace(rootRefreshToken))
            {
                return new UnauthorizedObjectResult(new { error = OAuthError.SessionExpired });
            }

            var rootRefreshCacheRaw = await _cacheClient.GetStringValueAsync(rootRefreshToken);
            var rootRefreshCache = string.IsNullOrWhiteSpace(rootRefreshCacheRaw)
                ? null
                : JsonSerializer.Deserialize<RefreshTokenCache>(rootRefreshCacheRaw);

            if (rootRefreshCache == null || rootRefreshCache.ExpiresUtc <= DateTime.UtcNow)
            {
                return new UnauthorizedObjectResult(new { error = OAuthError.SessionExpired });
            }

            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            // Check for organization switch within existing impersonation
            var existingSessionId = string.IsNullOrWhiteSpace(bc.ImpersonationSessionId) ? request.ImpersonationId : bc.ImpersonationSessionId;

            if (!string.IsNullOrWhiteSpace(existingSessionId))
            {
                var switchResult = await TrySwitchOrganizationContextAsync(
                    existingSessionId,
                    request,
                    userId,
                    user,
                    rootTenant,
                    rootRefreshCache,
                    authConfiguration,
                    httpRequest,
                    httpResponse,
                    rootDomain,
                    bc.UserId);

                if (switchResult != null)
                {
                    return switchResult;
                }
            }

            try
            {

                var clientId = rootRefreshCache.ClientId;
                if (string.IsNullOrWhiteSpace(clientId) || !await HasOidcClientConfigurationAsync(clientId))
                {
                    return new ObjectResult(new
                    {
                        error = "invalid_client",
                        error_description = "Client configuration not found"
                    })
                    {
                        StatusCode = StatusCodes.Status401Unauthorized
                    };
                }

                string sessionId;
                try
                {
                    sessionId = await _authSession.CreateAndBackupImpersonationSessionAsync(
                        userId,
                        rootTenant.TenantId,
                        request.TargetTenantId,
                        clientId,
                        request.OrganizationId ?? "default");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create impersonation session record for user {UserId}", userId);
                    return new ObjectResult(new { error = OAuthError.SessionCreationFailed, error_description = "Failed to create impersonation session" })
                    {
                        StatusCode = StatusCodes.Status500InternalServerError
                    };
                }

                _logger.LogInformation("Impersonation started by user {UserId} from root tenant {RootTenantId} to target tenant {TargetTenantId}", userId, rootTenant.TenantId, request.TargetTenantId);
                await WriteImpersonationAuditEventAsync(httpRequest, LoginAuditEvents.ImpersonationStarted, userId, request.TargetTenantId, IdpConstants.SeverityInfo, IdpConstants.StatusSuccess, rootTenant.TenantId);

                var tokenRequest = new TokenRequest
                {
                    GrantType = GrantTypes.ImpersonationCloud,
                    ClientId = clientId,
                    OrganizationId = !string.IsNullOrWhiteSpace(request.OrganizationId)
                        ? request.OrganizationId
                        : (string.IsNullOrWhiteSpace(request.OrganizationId) ? "default" : request.OrganizationId),
                    IsImpersonation = true,
                    OriginalTenantId = rootTenant.TenantId,
                    TargetTenantId = request.TargetTenantId,
                    ImpersonatorUserId = userId,
                    ImpersonationSessionId = sessionId,
                    Request = httpRequest
                };

                var tokenResponse = await _authSession.ManageTokenAsync(tokenRequest, authConfiguration, user);
                await _authSession.RevokeRefreshToken(rootRefreshToken);
                var cookiesSet = AppendCookies(tokenResponse, httpResponse, rootDomain);

                if (!cookiesSet)
                {
                    return new OkObjectResult(new
                    {
                        impersonation_mode = true,
                        access_token = tokenResponse.AccessToken,
                        refresh_token = tokenResponse.RefreshToken,
                        token_type = tokenResponse.TokenType,
                        expires_in = tokenResponse.ExpiresIn,
                        scope = tokenResponse.Scope,
                        id_token = tokenResponse.IdToken,
                        cookie_set = false,
                        impersonation_session_id = sessionId
                    });
                }

                // var sessionCookieOptions = DomainResolver.CreateCookieOptions(rootCookieDomain, rootRefreshCache.ExpiresUtc);

                //  httpResponse.Cookies.Delete(IdpConstants.ImpersonationIdCookieName, sessionCookieOptions);
                //  httpResponse.Cookies.Append(IdpConstants.ImpersonationIdCookieName, sessionId, sessionCookieOptions);

                return new OkObjectResult(new ImpersonateResponse { impersonation_mode = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during impersonation for user {UserId} to tenant {TargetTenantId}", userId, request.TargetTenantId);
                return new ObjectResult(new { error = "impersonation_failed", error_description = "An unexpected error occurred during impersonation" })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }

        }
        private static bool AppendCookies(TokenResponse response, HttpResponse httpResponse, string domain)
        {
            return CookieHelper.AppendCookies(response, httpResponse, domain);
        }

        private static void DeleteCookie(HttpResponse httpResponse, string domain, CookieOptions accessCookieOptions, CookieOptions refreshCookieOptions)
        {
            CookieHelper.DeleteAccessAndRefreshTokenCookies(httpResponse, domain, accessCookieOptions, refreshCookieOptions);
        }


        private static void ClearImpersonationCookies(HttpResponse httpResponse, string domain, string cookieDomain)
        {
            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(cookieDomain))
            {
                return;
            }
            var options = DomainResolver.CreateCookieOptions(cookieDomain, DateTime.UtcNow.AddDays(-1));
            // httpResponse.Cookies.Delete(IdpConstants.ImpersonationIdCookieName, options);
            httpResponse.Cookies.Delete($"{IdpConstants.RefreshTokenCookieName}_{domain}", options);
            httpResponse.Cookies.Delete(domain, options);
        }

        private async Task WriteImpersonationAuditEventAsync(HttpRequest httpRequest, string eventType, string userId, string targetTenantId, string severity, string status, string rootTenantId)
        {
            try
            {
                var entry = new AuditLogModel
                {
                    EventType = eventType,
                    UserId = userId,
                    TenantId = rootTenantId,
                    Severity = severity,
                    Status = status,
                    Timestamp = DateTime.UtcNow,
                    IpAddress = httpRequest.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = httpRequest.Headers.UserAgent.ToString(),
                    Details = string.IsNullOrWhiteSpace(targetTenantId) ? null : $"target_tenant={targetTenantId}"
                };

                await _auditLogRepository.CreateAsync(entry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist impersonation audit event {EventType}", eventType);
            }
        }


        public async Task<IActionResult> ExecuteStopImpersonationAsync(StopImpersonationRequest request, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            var bc = BlocksContext.GetContext();
            ImpersonationSession? session = null;
            var rootTenant = _tenants.GetTenantByID(bc!.OriginalTenantId);
            var (rootDomain, rootCookieDomain, _) = DomainResolver.ResolveDomain(rootTenant, httpRequest);

            var impersonationSessionId = string.IsNullOrWhiteSpace(request.ImpersonationId)
                ? bc!.ImpersonationSessionId
                : request.ImpersonationId;

            if (!string.IsNullOrWhiteSpace(impersonationSessionId))
            {
                session = await _authenticationRepository.GetImpersonationSessionByIdAsync(impersonationSessionId);
            }

            if (session == null)
            {
                await WriteImpersonationAuditEventAsync(httpRequest, LoginAuditEvents.ImpersonationStopFailed, bc!.UserId, null, IdpConstants.SeverityWarn, OAuthError.SessionNotFound, rootTenant.TenantId);
                ClearImpersonationCookies(httpResponse, rootDomain, rootCookieDomain);
                return new UnauthorizedObjectResult(new { error = OAuthError.SessionExpired });
            }

            var refreshToken = string.IsNullOrWhiteSpace(request.RefreshToken)
                ? CookieToken(httpRequest)
                : request.RefreshToken;

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return new UnauthorizedObjectResult(new { error = "invalid_refresh_token" });
            }

            var rootRefreshCacheRaw = await _cacheClient.GetStringValueAsync(refreshToken);
            var rootRefreshCache = string.IsNullOrWhiteSpace(rootRefreshCacheRaw)
                ? null
                : JsonSerializer.Deserialize<RefreshTokenCache>(rootRefreshCacheRaw);

            if (rootRefreshCache == null || !rootRefreshCache.Impersonated)
            {
                return new UnauthorizedObjectResult(new { error = "invalid_refresh_token" });
            }

            var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.ImpersonationCloud,
                ClientId = !string.IsNullOrWhiteSpace(session.ClientId) ? session.ClientId : rootRefreshCache.ClientId,
                OrganizationId = session.OrganizationId,
                IsImpersonation = false,
                OriginalTenantId = session.RootTenantId,
                TargetTenantId = session.TargetTenantId,
                ImpersonatorUserId = bc.UserId,
                Request = httpRequest
            };

            // Get root user
            var rootUser = !string.IsNullOrWhiteSpace(bc!.UserId) ? await _authenticationRepository.GetUserByIdAsync(bc!.UserId) : null;
            if (rootUser == null)
            {
                await WriteImpersonationAuditEventAsync(httpRequest, LoginAuditEvents.ImpersonationStopFailed, bc!.UserId, session.TargetTenantId, IdpConstants.SeverityWarn, OAuthError.RootUserNotFound, rootTenant.TenantId);
                ClearImpersonationCookies(httpResponse, rootDomain, rootCookieDomain);
                return new BadRequestObjectResult(new { error = "session_expired" });
            }

            var tokenResponse = await _authSession.ManageTokenAsync(tokenRequest, configuration, rootUser);
            if (!string.IsNullOrWhiteSpace(tokenResponse.Error))
            {
                await WriteImpersonationAuditEventAsync(httpRequest, LoginAuditEvents.ImpersonationStopFailed, bc!.UserId, session.TargetTenantId, IdpConstants.SeverityWarn, tokenResponse.Error, rootTenant.TenantId);
                ClearImpersonationCookies(httpResponse, rootDomain, rootCookieDomain);
                return new BadRequestObjectResult(new { error = tokenResponse.Error });
            }

            try
            {
                var updates = new Dictionary<string, object>
                    {
                        { "status", "ended_by_admin_stop" },
                        { "ended_at", DateTime.UtcNow }
                    };
                await _authenticationRepository.UpdateImpersonationSessionAsync(impersonationSessionId, updates);
                // httpResponse.Cookies.Delete(IdpConstants.ImpersonationIdCookieName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup impersonation session {SessionId}", impersonationSessionId);
            }

            await WriteImpersonationAuditEventAsync(httpRequest, LoginAuditEvents.ImpersonationStopped, bc!.UserId, session.TargetTenantId, IdpConstants.SeverityInfo, IdpConstants.StatusSuccess, rootTenant.TenantId);

            await _authSession.RevokeRefreshToken(refreshToken);

            var cookiesSet = AppendCookies(tokenResponse, httpResponse, rootDomain);
            if (!cookiesSet)
            {
                return new OkObjectResult(new
                {
                    impersonation_mode = false,
                    access_token = tokenResponse.AccessToken,
                    refresh_token = tokenResponse.RefreshToken,
                    token_type = tokenResponse.TokenType,
                    expires_in = tokenResponse.ExpiresIn,
                    scope = tokenResponse.Scope,
                    id_token = tokenResponse.IdToken,
                    cookie_set = false,
                });
            }
            _logger.LogInformation("Impersonation stopped manually and root session restored");
            return new OkObjectResult(new StopImpersonationResponse { impersonation_mode = false });
        }


        private async Task<(IActionResult? Error, string UserId, Iam.DomainService.Entities.User User)> ValidateImpersonationRequestAsync(
            ImpersonateRequest request,
            Tenant? rootTenant,
            HttpRequest httpRequest)
        {
            if (rootTenant == null || !rootTenant.IsRootTenant)
            {
                return (new ObjectResult(new
                {
                    error = "forbidden",
                    error_description = "Only root-tenant users are allowed to start impersonation"
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                }, string.Empty, null!);
            }

            if (string.IsNullOrWhiteSpace(request.TargetTenantId))
            {
                return (new BadRequestObjectResult(new { error = "invalid_request", error_description = "target_tenant_id is required" }), string.Empty, null!);
            }

            var targetTenant = _tenants.GetTenantByID(request.TargetTenantId);
            if (targetTenant == null)
            {
                return (new BadRequestObjectResult(new { error = "invalid_target_tenant", error_description = "Target tenant does not exist" }), string.Empty, null!);
            }

            var bc = BlocksContext.GetContext();
            var userId = string.IsNullOrWhiteSpace(bc!.UserId) ? request.ImpersontingUserId : bc.UserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return (new UnauthorizedObjectResult(new { error = "invalid_user" }), string.Empty, null!);
            }

            var user = await _authenticationRepository.GetUserByIdAsync(userId);
            if (user == null)
            {
                return (new UnauthorizedObjectResult(new { error = "invalid_user" }), string.Empty, null!);
            }

            var isSharedWithUser = await IsTenantSharedWithUserAsync(userId, request.TargetTenantId);
            if (!isSharedWithUser)
            {
                await WriteImpersonationAuditEventAsync(httpRequest, LoginAuditEvents.ImpersonationStartDenied, userId, request.TargetTenantId, IdpConstants.SeverityWarn, OAuthError.NotSharedWithUser, rootTenant.TenantId);
                return (new ObjectResult(new
                {
                    error = "forbidden",
                    error_description = "Target tenant is not shared with the requesting user"
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                }, string.Empty, user);
            }

            return (null, userId, user);
        }

        private async Task<IActionResult?> TrySwitchOrganizationContextAsync(
            string existingSessionId,
            ImpersonateRequest request,
            string userId,
            Iam.DomainService.Entities.User user,
            Tenant rootTenant,
            RefreshTokenCache? rootRefreshCache,
            IdentityConfiguration authConfiguration,
            HttpRequest httpRequest,
            HttpResponse httpResponse,
            string? rootDomain,
            string? bcUserId)
        {
            var existingSession = await _authenticationRepository.GetImpersonationSessionByIdAsync(existingSessionId);
            if (existingSession == null
                || existingSession.Status != "active"
                || !string.Equals(existingSession.TargetTenantId, request.TargetTenantId, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var switchOrgSuccess = await _authSession.SwitchOrganizationContextAsync(
                existingSessionId,
                request.OrganizationId ?? "default");

            if (!switchOrgSuccess)
            {
                return null;
            }

            try
            {
                var newTokenRequest = new TokenRequest
                {
                    GrantType = GrantTypes.ImpersonationCloud,
                    ClientId = rootRefreshCache?.ClientId,
                    OrganizationId = request.OrganizationId ?? "default",
                    IsImpersonation = true,
                    OriginalTenantId = rootTenant.TenantId,
                    TargetTenantId = request.TargetTenantId,
                    ImpersonatorUserId = userId,
                    Request = httpRequest,
                    ImpersonationSessionId = existingSessionId,
                };

                var newTokenResponse = await _authSession.ManageTokenAsync(newTokenRequest, authConfiguration, user);

                if (!string.IsNullOrWhiteSpace(newTokenResponse.Error))
                {
                    _logger.LogError("Token issuance failed during org switch for session {SessionId}. Error: {Error}", existingSessionId, newTokenResponse.Error);
                    return new ObjectResult(new { error = newTokenResponse.Error, error_description = "Failed to issue new tokens after organization switch" })
                    {
                        StatusCode = StatusCodes.Status500InternalServerError
                    };
                }

                await WriteImpersonationAuditEventAsync(httpRequest, "org_switched", userId, request.TargetTenantId, IdpConstants.SeverityInfo, IdpConstants.StatusSuccess, rootTenant.TenantId);

                var cookiesSet = AppendCookies(newTokenResponse, httpResponse, rootDomain);
                if (cookiesSet)
                {
                    _logger.LogInformation("Organization switched by user {UserId} in impersonation session {SessionId} to org {OrgId} with new tokens", bcUserId, existingSessionId, request.OrganizationId);
                    return new OkObjectResult(new ImpersonateResponse { impersonation_mode = true, org_switched = true });
                }

                return new OkObjectResult(new
                {
                    impersonation_mode = true,
                    org_switched = true,
                    access_token = newTokenResponse.AccessToken,
                    refresh_token = newTokenResponse.RefreshToken,
                    token_type = newTokenResponse.TokenType,
                    cookie_set = false
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during organization switch for session {SessionId}", existingSessionId);
                return new ObjectResult(new { error = "org_switch_failed", error_description = "An unexpected error occurred during organization switch" })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }


    }
}
