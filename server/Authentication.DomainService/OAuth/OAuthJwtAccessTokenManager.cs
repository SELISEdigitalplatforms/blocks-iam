using Blocks.Genesis;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Microsoft.Extensions.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.Shared;

namespace Authentication.DomainService.OAuth
{
    public class OAuthJwtAccessTokenManager : IOAuthJwtAccessTokenManager
    {
        private const string IdpSessionCookieName = "idp_session_id";
        private readonly IJwtAccessTokenProvider _jwtAccessTokenProvider;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IOtpServiceFactory _otpServiceFactory;
        private readonly IMfaConfigurationService _configurationService;
        private readonly IConfiguration _configuration;
        private readonly ICacheClient _cacheClient;
        private readonly ITenants _tenants;
        private readonly UnifiedTokenSessionService _unifiedTokenSessionService;

        public OAuthJwtAccessTokenManager(
            IJwtAccessTokenProvider jwtAccessTokenProvider,
            IAuthenticationDomainService authenticationDomainService,
            IAuthenticationRepository authenticationRepository,
            IMfaConfigurationService configurationService,
            ICacheClient cacheClient,
            ITenants tenants,
            IOtpServiceFactory otpServiceFactory,
            IConfiguration configuration,
            UnifiedTokenSessionService unifiedTokenSessionService
        )
        {
            _jwtAccessTokenProvider = jwtAccessTokenProvider;
            _authenticationDomainService = authenticationDomainService;
            _authenticationRepository = authenticationRepository;
            _configurationService = configurationService;
            _cacheClient = cacheClient;
            _tenants = tenants;
            _otpServiceFactory = otpServiceFactory;
            _configuration = configuration;
            _unifiedTokenSessionService = unifiedTokenSessionService;
        }

        public async Task<TokenResponse> ManageTokenAsync(TokenRequest tokenRequest, AuthenticationConfiguration authenticationConfiguration, User user, StateInfo? stateInfo = null)
        {
            var bc = BlocksContext.GetContext();
            
            var tokenResponse = await ProcessCheckPoints(tokenRequest, user);

            if (tokenResponse != null)
            {
                return tokenResponse;
            }

            var tenant = _tenants.GetTenantByID(bc?.TenantId ?? "");
            if (tenant == null)
            {
                return new TokenResponse
                {
                    Error = "invalid_tenant",
                    ErrorDescription = "Tenant not found",
                    StatusCode = 400
                };
            }

            var issuanceContext = new TokenIssuanceContext
            {
                IsImpersonation = tokenRequest.IsImpersonation,
                OriginalTenantId = tokenRequest.OriginalTenantId,
                ActorUserId = tokenRequest.ImpersonatorUserId
            };
            var (clientAllowedScopes, allowedServiceAccessResources) = await ResolveClientAuthorizationConfigAsync(tokenRequest.ClientId);
            var jwtAccessToken = await _jwtAccessTokenProvider.GetJwtAccessToken(
                authenticationConfiguration,
                tenant,
                user,
                tokenRequest.TargetTenantId,
                stateInfo,
                organizationId: tokenRequest.OrganizationId,
                issuanceContext: issuanceContext,
                clientAllowedScopes: clientAllowedScopes,
                clientAllowedServiceAccessResources: allowedServiceAccessResources);

            var accessToken = CreateJwtAccessToken(jwtAccessToken);
            var (refreshToken, refreshValidity) = await ManageRefreshTokenAsync(tokenRequest, jwtAccessToken, authenticationConfiguration, tenant, user);
            var (_, cookieDomain, _) = DomainResolver.ResolveDomain(tenant, tokenRequest.Request);

            return new TokenResponse
            {
                AccessToken = accessToken,
                ExpiresIn = authenticationConfiguration.AccessTokenValidForNumberMinutes,
                ExpiresUtc = jwtAccessToken.Expires,
                RefreshToken = refreshToken,
                RefreshExpiresUtc = refreshValidity,
                CookieDomain = cookieDomain,
                StatusCode = 200
            };
        }

        private async Task<(IReadOnlyCollection<string> AllowedScopes, IReadOnlyCollection<string> AllowedServiceAccessResources)> ResolveClientAuthorizationConfigAsync(string? clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return ([], []);
            }

            var oidcClient = await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
            var allowedScopes = oidcClient?.AllowedScopes?
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? [];

            var allowedServiceAccessResources = oidcClient?.AllowedServiceAccessResources?
                .Where(resource => !string.IsNullOrWhiteSpace(resource))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? [];

            return (allowedScopes, allowedServiceAccessResources);
        }

        private async Task<TokenResponse?> ProcessCheckPoints(TokenRequest tokenRequest, User user)
        {
            if (tokenRequest.GrantType != GrantTypes.MfaCode && tokenRequest.GrantType != GrantTypes.ClientCredential && await CheckIfMfaIsApplicable(user))
            {
                return await HandleMfaAuthentication(user);
            }

            return null;
        }

        private async Task<TokenResponse> HandleMfaAuthentication(User user)
        {
            try
            {
                var otpService = _otpServiceFactory.GetOTPService(user.UserMfaType);
                if (otpService == null)
                {
                    return new TokenResponse
                    {
                        Error = "server_error",
                        ErrorDescription = "Mfa provider is not available",
                        StatusCode = 500
                    };
                }

                var response = await otpService.GenerateAsync(new UserInfo
                {
                    Email = user.Email,
                    ItemId = user.ItemId,
                    Language = user.Language ?? "en-US"
                });

                if (response == null || string.IsNullOrWhiteSpace(response.MfaId))
                {
                    return new TokenResponse
                    {
                        Error = "server_error",
                        ErrorDescription = "Failed to generate mfa challenge",
                        StatusCode = 500
                    };
                }

                return new TokenResponse
                {
                    MfaId = response.MfaId,
                    UserMfa = user.UserMfaType,
                    Error = OAuthError.MfaEnabled,
                    ErrorDescription = "Mfa code required",
                    StatusCode = 200
                };
            }
            catch
            {
                return new TokenResponse
                {
                    Error = "server_error",
                    ErrorDescription = "Unable to initiate mfa challenge",
                    StatusCode = 500
                };
            }
        }


        private async Task<bool> CheckIfMfaIsApplicable(User user)
        {
            var mfaConfiguration = await _configurationService.GetAsync();
            var mfaProviders = mfaConfiguration.UserMfaType ?? [];

            return user.MfaEnabled && mfaProviders.Contains(user.UserMfaType);
        }

        public static string CreateJwtAccessToken(JwtAccessToken jwtAccessToken, StateInfo? stateInfo = null)
        {
            

            var jwtToken = new JwtSecurityToken(
                jwtAccessToken.Issuer,
                jwtAccessToken.Audience,
                jwtAccessToken.Claims,
                jwtAccessToken.NotBefore,
                jwtAccessToken.Expires,
                jwtAccessToken.SigningCredentials);

            return new JwtSecurityTokenHandler().WriteToken(jwtToken);
        }

        public async Task<(string, DateTime)> ManageRefreshTokenAsync(TokenRequest tokenRequest, JwtAccessToken jwtAccessToken, AuthenticationConfiguration authenticationConfiguration, Tenant tenant, User user)
        {
            var visitorsIpAddresses = _authenticationDomainService.GetVisitorsIpAddresses(tokenRequest.Request.HttpContext) ?? new List<string>();
            // Unify both initial and rotation flows
            if (tokenRequest.GrantType == GrantTypes.RefreshToken || tokenRequest.GrantType == GrantTypes.SwitchOrganization)
            {
                // Rotation: fetch old token from cache
                var oldRefreshTokenCacheStr = await _cacheClient.GetStringValueAsync(tokenRequest.RefreshToken);
                RefreshTokenCache? oldRefreshTokenCache = null;
                if (!string.IsNullOrWhiteSpace(oldRefreshTokenCacheStr))
                {
                    oldRefreshTokenCache = JsonSerializer.Deserialize<RefreshTokenCache>(oldRefreshTokenCacheStr);
                }
                return await _unifiedTokenSessionService.CreateOrRotateRefreshToken(
                    tokenRequest.RefreshToken,
                    oldRefreshTokenCache,
                    tokenRequest,
                    authenticationConfiguration,
                    tenant,
                    user,
                    visitorsIpAddresses
                );
            }
            else
            {
                // Initial auth flow: no old token
                return await _unifiedTokenSessionService.CreateOrRotateRefreshToken(
                    null,
                    null,
                    tokenRequest,
                    authenticationConfiguration,
                    tenant,
                    user,
                    visitorsIpAddresses
                );
            }
        }

        private async Task<(string, DateTime)> HandleRefreshTokenGrant(TokenRequest tokenRequest, Tenant tenant, User user, IEnumerable<string> visitorsIpAddresses, AuthenticationConfiguration authenticationConfiguration)
        {
            // Validate refresh token exists
            if (string.IsNullOrWhiteSpace(tokenRequest.RefreshToken))
            {
                return (string.Empty, DateTime.MinValue);
            }

            // Case 1: Check if refresh token exists in Redis
            var oldRefreshTokenCache = await _cacheClient.GetStringValueAsync(tokenRequest.RefreshToken);
            
            if (string.IsNullOrEmpty(oldRefreshTokenCache))
            {
                await HandleRefreshTokenReuseAsync(tokenRequest.RefreshToken);
                return (string.Empty, DateTime.MinValue);
            }

            var oldRefreshToken = JsonSerializer.Deserialize<RefreshTokenCache>(oldRefreshTokenCache);
            if (oldRefreshToken == null)
            {
                // Case 2: Invalid token data
                return (string.Empty, DateTime.MinValue);
            }

            var presentedSessionId = tokenRequest.Request?.Cookies[IdpSessionCookieName];
            if (!IsBindingValid(oldRefreshToken, tokenRequest, presentedSessionId))
            {
                await _cacheClient.RemoveKeyAsync(tokenRequest.RefreshToken);
                return (string.Empty, DateTime.MinValue);
            }

            var now = DateTime.UtcNow;

            // Absolute lifetime hard-stop.
            var absoluteExpiresUtc = oldRefreshToken.AbsoluteExpiresUtc == default
                ? oldRefreshToken.ExpiresUtc
                : oldRefreshToken.AbsoluteExpiresUtc;

            // Check RememberMe absolute expiry if applicable
            if (oldRefreshToken.RememberMe && oldRefreshToken.RememberMeExpiresUtc.HasValue && now >= oldRefreshToken.RememberMeExpiresUtc.Value)
            {
                // RememberMe window has expired
                await _cacheClient.RemoveKeyAsync(tokenRequest.RefreshToken);
                
                var revokeRememberMeEvent = new RefreshTokenEvent
                {
                    RefreshToken = tokenRequest.RefreshToken ?? string.Empty,
                    TenantId = oldRefreshToken.TenantId,
                    OrganizationId = oldRefreshToken.OrganizationId,
                    ClientId = oldRefreshToken.ClientId,
                    SessionId = oldRefreshToken.SessionId,
                    IssuedUtc = oldRefreshToken.IssuedUtc,
                    ExpiresUtc = oldRefreshToken.ExpiresUtc,
                    IpAddresses = oldRefreshToken.IpAddresses ?? string.Empty,
                    UserId = oldRefreshToken.UserId ?? string.Empty,
                    DeviceInformation = _authenticationDomainService.GetDeviceInfo(tokenRequest.Request?.Headers?.UserAgent ?? string.Empty),
                    IsRevoke = true,
                    IsLogin = false,
                    GrantType = tokenRequest.GrantType
                };
                await _authenticationDomainService.SendToQueueAsync(Utilities.IdpConstants.AuthenticationQueue, revokeRememberMeEvent);
                
                return (string.Empty, DateTime.MinValue);
            }

            // Security-stamp/token-version invalidation: deny refresh if user credentials/session version changed.
            if (oldRefreshToken.TokenVersion != user.TokenVersion)
            {
                await _cacheClient.RemoveKeyAsync(tokenRequest.RefreshToken);
                return (string.Empty, DateTime.MinValue);
            }

            // token valid if: now < expires_at AND now < absolute_expiry
            if (now >= oldRefreshToken.ExpiresUtc || now >= absoluteExpiresUtc)
            {
                // Delete expired token and send revocation event
                await _cacheClient.RemoveKeyAsync(tokenRequest.RefreshToken);
                
                var revokeEvent = new RefreshTokenEvent
                {
                    RefreshToken = tokenRequest.RefreshToken ?? string.Empty,
                    TenantId = oldRefreshToken.TenantId,
                    OrganizationId = oldRefreshToken.OrganizationId,
                    ClientId = oldRefreshToken.ClientId,
                    SessionId = oldRefreshToken.SessionId,
                    IssuedUtc = oldRefreshToken.IssuedUtc,
                    ExpiresUtc = oldRefreshToken.ExpiresUtc,
                    IpAddresses = oldRefreshToken.IpAddresses ?? string.Empty,
                    UserId = oldRefreshToken.UserId ?? string.Empty,
                    DeviceInformation = _authenticationDomainService.GetDeviceInfo(tokenRequest.Request?.Headers?.UserAgent ?? string.Empty),
                    IsRevoke = true,
                    IsLogin = false,
                    GrantType = tokenRequest.GrantType
                };
                await _authenticationDomainService.SendToQueueAsync(Utilities.IdpConstants.AuthenticationQueue, revokeEvent);
                
                return (string.Empty, DateTime.MinValue);
            }

            // Case 1: Token exists and has sufficient TTL - rotate token
            var newRefreshTokenId = Guid.NewGuid().ToString("N");
            var configuredSlidingMinutes = oldRefreshToken.RememberMe
                ? (authenticationConfiguration.RememberMeRefreshTokenValidForNumberMinutes > 0
                    ? authenticationConfiguration.RememberMeRefreshTokenValidForNumberMinutes
                    : authenticationConfiguration.RefreshTokenValidForNumberMinutes)
                : (authenticationConfiguration.RefreshTokenValidForNumberMinutes > 0
                    ? authenticationConfiguration.RefreshTokenValidForNumberMinutes
                    : 15);

            // Sliding expiry always comes from configuration.
            var newRefreshTokenExpireOn = now.AddMinutes(configuredSlidingMinutes);
            if (newRefreshTokenExpireOn > absoluteExpiresUtc)
            {
                newRefreshTokenExpireOn = absoluteExpiresUtc;
            }

            var effectiveSliding = newRefreshTokenExpireOn - now;
            var effectiveSlidingSeconds = (int)Math.Ceiling(effectiveSliding.TotalSeconds);

            if (effectiveSlidingSeconds <= 0)
            {
                await _cacheClient.RemoveKeyAsync(tokenRequest.RefreshToken);
                return (string.Empty, DateTime.MinValue);
            }

            var newRefreshTokenCache = new RefreshTokenCache
            {
                RefreshToken = newRefreshTokenId,
                TenantId = oldRefreshToken.TenantId,
                OrganizationId = oldRefreshToken.OrganizationId,
                ClientId = oldRefreshToken.ClientId,
                SessionId = oldRefreshToken.SessionId,
                IssuedUtc = now,
                ExpiresUtc = newRefreshTokenExpireOn,
                AbsoluteExpiresUtc = absoluteExpiresUtc,
                IpAddresses = string.Join(",", visitorsIpAddresses),
                UserId = oldRefreshToken.UserId ?? string.Empty,
                AuthMode = oldRefreshToken.AuthMode,
                OriginalTenantId = oldRefreshToken.OriginalTenantId,
                TargetTenantId = oldRefreshToken.TargetTenantId,
                ImpersonatorUserId = oldRefreshToken.ImpersonatorUserId,
                RememberMe = oldRefreshToken.RememberMe,
                TokenVersion = oldRefreshToken.TokenVersion,
                RememberMeIssuedUtc = oldRefreshToken.RememberMeIssuedUtc,
                RememberMeExpiresUtc = oldRefreshToken.RememberMeExpiresUtc,
                Scope = oldRefreshToken.Scope
            };

            // Save new token to Redis with precise remaining TTL
            await _cacheClient.AddStringValueAsync(newRefreshTokenId, JsonSerializer.Serialize(newRefreshTokenCache), effectiveSlidingSeconds);

            // Delete old token from Redis
            await _cacheClient.RemoveKeyAsync(tokenRequest.RefreshToken);

            // Send revocation event for old token
            var revokeOldTokenEvent = new RefreshTokenEvent
            {
                RefreshToken = tokenRequest.RefreshToken ?? string.Empty,
                TenantId = oldRefreshToken.TenantId,
                OrganizationId = oldRefreshToken.OrganizationId,
                ClientId = oldRefreshToken.ClientId,
                SessionId = oldRefreshToken.SessionId,
                IssuedUtc = oldRefreshToken.IssuedUtc,
                ExpiresUtc = oldRefreshToken.ExpiresUtc,
                IpAddresses = oldRefreshToken.IpAddresses ?? string.Empty,
                UserId = oldRefreshToken.UserId ?? string.Empty,
                DeviceInformation = _authenticationDomainService.GetDeviceInfo(tokenRequest.Request?.Headers?.UserAgent ?? string.Empty),
                IsRevoke = true,
                IsLogin = false,
                GrantType = tokenRequest.GrantType
            };
            await _authenticationDomainService.SendToQueueAsync(Utilities.IdpConstants.AuthenticationQueue, revokeOldTokenEvent);

            // Send creation event for new token (renewal, not login)
            var addNewTokenEvent = new RefreshTokenEvent
            {
                RefreshToken = newRefreshTokenCache.RefreshToken,
                TenantId = newRefreshTokenCache.TenantId,
                OrganizationId = newRefreshTokenCache.OrganizationId,
                ClientId = newRefreshTokenCache.ClientId,
                SessionId = newRefreshTokenCache.SessionId,
                IssuedUtc = newRefreshTokenCache.IssuedUtc,
                ExpiresUtc = newRefreshTokenCache.ExpiresUtc,
                IpAddresses = newRefreshTokenCache.IpAddresses,
                UserId = newRefreshTokenCache.UserId,
                DeviceInformation = _authenticationDomainService.GetDeviceInfo(tokenRequest.Request?.Headers?.UserAgent ?? string.Empty),
                IsRevoke = false,
                IsLogin = false,
                GrantType = tokenRequest.GrantType
            };
            await _authenticationDomainService.SendToQueueAsync(Utilities.IdpConstants.AuthenticationQueue, addNewTokenEvent);

            return (newRefreshTokenId, newRefreshTokenExpireOn);
        }

        private async Task<(string, DateTime)> CreateNewRefreshToken(TokenRequest tokenRequest, Tenant tenant, User user, AuthenticationConfiguration authenticationConfiguration, IEnumerable<string> visitorsIpAddresses)
        {
            var refreshTokenId = Guid.NewGuid().ToString("N");

            // Initial auth flow - use full configured lifetime
            var configuredRefreshTokenLifetime = authenticationConfiguration.RefreshTokenValidForNumberMinutes > 0
                ? authenticationConfiguration.RefreshTokenValidForNumberMinutes
                : 15;

            var configuredRememberMeLifetime = authenticationConfiguration.RememberMeRefreshTokenValidForNumberMinutes > 0
                ? authenticationConfiguration.RememberMeRefreshTokenValidForNumberMinutes
                : configuredRefreshTokenLifetime;

            var refreshTokenLifetime = tokenRequest.RememberMe
                ? configuredRememberMeLifetime
                : configuredRefreshTokenLifetime;

            var configuredAbsoluteLifetime = tokenRequest.RememberMe
                ? (authenticationConfiguration.RememberMeAbsoluteRefreshTokenValidForNumberMinutes > 0
                    ? authenticationConfiguration.RememberMeAbsoluteRefreshTokenValidForNumberMinutes
                    : authenticationConfiguration.AbsoluteRefreshTokenValidForNumberMinutes)
                : (authenticationConfiguration.AbsoluteRefreshTokenValidForNumberMinutes > 0
                    ? authenticationConfiguration.AbsoluteRefreshTokenValidForNumberMinutes
                    : configuredRememberMeLifetime);

            if (configuredAbsoluteLifetime < refreshTokenLifetime)
            {
                configuredAbsoluteLifetime = refreshTokenLifetime;
            }

            var now = DateTime.UtcNow;
            var refreshTokenExpireOn = now.AddMinutes(refreshTokenLifetime);
            var absoluteRefreshTokenExpireOn = now.AddMinutes(configuredAbsoluteLifetime);

            var refreshTokenCache = new RefreshTokenCache
            {
                RefreshToken = refreshTokenId,
                TenantId = tenant.TenantId,
                OrganizationId = tokenRequest.OrganizationId,
                ClientId = tokenRequest.ClientId,
                SessionId = tokenRequest.Request?.Cookies[IdpSessionCookieName],
                IssuedUtc = now,
                ExpiresUtc = refreshTokenExpireOn,
                AbsoluteExpiresUtc = absoluteRefreshTokenExpireOn,
                IpAddresses = string.Join(",", visitorsIpAddresses),
                UserId = user.ItemId ?? string.Empty,
                AuthMode = tokenRequest.IsImpersonation ? "impersonation" : "root",
                OriginalTenantId = tokenRequest.OriginalTenantId,
                TargetTenantId = tokenRequest.TargetTenantId,
                ImpersonatorUserId = tokenRequest.ImpersonatorUserId,
                RememberMe = tokenRequest.RememberMe,
                TokenVersion = user.TokenVersion,
                RememberMeIssuedUtc = tokenRequest.RememberMe ? now : null,
                RememberMeExpiresUtc = tokenRequest.RememberMe ? absoluteRefreshTokenExpireOn : null,
                Scope = tokenRequest.Scope
            };

            await _cacheClient.AddStringValueAsync(refreshTokenCache.RefreshToken, JsonSerializer.Serialize(refreshTokenCache), refreshTokenLifetime * 60);

            var addRefreshTokenCommand = new RefreshTokenEvent
            {
                RefreshToken = refreshTokenCache.RefreshToken,
                TenantId = refreshTokenCache.TenantId,
                OrganizationId = refreshTokenCache.OrganizationId,
                ClientId = refreshTokenCache.ClientId,
                SessionId = refreshTokenCache.SessionId,
                IssuedUtc = refreshTokenCache.IssuedUtc,
                ExpiresUtc = refreshTokenCache.ExpiresUtc,
                IpAddresses = refreshTokenCache.IpAddresses,
                UserId = refreshTokenCache.UserId,
                DeviceInformation = _authenticationDomainService.GetDeviceInfo(tokenRequest.Request?.Headers?.UserAgent ?? string.Empty),
                IsRevoke = false,
                IsLogin = true,
                GrantType = tokenRequest.GrantType
            };
            
            await _authenticationDomainService.SendToQueueAsync(Utilities.IdpConstants.AuthenticationQueue, addRefreshTokenCommand);

            return (refreshTokenId, refreshTokenExpireOn);
        }

        private static bool IsBindingValid(RefreshTokenCache cachedToken, TokenRequest request, string? presentedSessionId)
        {
            var currentTenantId = BlocksContext.GetContext()?.TenantId;
            if (!string.IsNullOrWhiteSpace(cachedToken.TenantId)
                && !string.IsNullOrWhiteSpace(currentTenantId)
                && !string.Equals(cachedToken.TenantId, currentTenantId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(cachedToken.OrganizationId)
                && !string.IsNullOrWhiteSpace(request.OrganizationId)
                && !string.Equals(cachedToken.OrganizationId, request.OrganizationId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(cachedToken.ClientId)
                && !string.IsNullOrWhiteSpace(request.ClientId)
                && !string.Equals(cachedToken.ClientId, request.ClientId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(cachedToken.SessionId)
                && !string.IsNullOrWhiteSpace(presentedSessionId)
                && !string.Equals(cachedToken.SessionId, presentedSessionId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private async Task HandleRefreshTokenReuseAsync(string refreshToken)
        {
            var existingSession = await _authenticationRepository.GetIdentitySessionByRefreshTokenAsync(refreshToken);
            if (existingSession == null || existingSession.IsActive)
            {
                return;
            }

            IEnumerable<IdentitySession> activeSessions;
            if (!string.IsNullOrWhiteSpace(existingSession.SessionId))
            {
                activeSessions = await _authenticationRepository.GetActiveIdentitySessionBySessionIdAsync(existingSession.SessionId);
            }
            else
            {
                activeSessions = await _authenticationRepository.GetActiveIdentitySessionByUserIdAsync(existingSession.UserId);
            }

            var refreshTokens = activeSessions
                .Select(x => x.RefreshToken)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (refreshTokens.Count == 0)
            {
                return;
            }

            await _authenticationRepository.RevokeIdentitySessionsByRefreshTokensAsync(refreshTokens!);

            foreach (var token in refreshTokens)
            {
                await _cacheClient.RemoveKeyAsync(token!);
            }
        }

        public TokenResponse ProcessAccountLock(AuthenticationConfiguration authenticationConfiguration, Tenant tenant, User user)
        {
            var lockKey = $"account-lock-{tenant.TenantId}-{user.ItemId}-{user.OrganizationIds?.FirstOrDefault() ?? "default"}";
            var isLocked = IsLocked(lockKey, authenticationConfiguration.GetNumberOfWrongAttemptsToLockTheAccount);

            if (!isLocked)
            {
                Lock(lockKey, authenticationConfiguration.AccountLockDurationInMinutes, authenticationConfiguration.GetNumberOfWrongAttemptsToLockTheAccount);
                return new TokenResponse();
            }

            return new TokenResponse { Error = OAuthError.AccountLocked, ErrorDescription = "Your account has been locked due to multiple failed login attempts" };
        }

        public void Lock(string key, int lockTimeInMinutes, int maxAttempts)
        {
            var lockCountValue = _cacheClient.GetStringValue(key);
            var lockCount = string.IsNullOrWhiteSpace(lockCountValue) ? 0 : int.Parse(lockCountValue);

            if (lockCount >= maxAttempts)
            {
                return;
            }

            _cacheClient.AddStringValue(key, (lockCount + 1).ToString(), lockTimeInMinutes * 60);
        }

        public bool IsLocked(string key, int maxAttempts)
        {
            var lockCountValue = _cacheClient.GetStringValue(key);

            return !string.IsNullOrWhiteSpace(lockCountValue) && int.Parse(lockCountValue) >= maxAttempts;
        }
    }
}