using Authentication.DomainService.Dtos;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Blocks.Genesis;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Security.Claims;
using System.Text.Json;

namespace Authentication.DomainService.Authentication
{
    public sealed class AuthenticationFlowService : IAuthenticationFlowService
    {
        /// <summary>Must match the reason UnifiedTokenSessionService writes when it rotates a token.</summary>
        private const string SupersededByRotationReason = "superseded_by_rotation";

        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IAuthStrategy _authStrategy;
        private readonly ITokenRefresher _tokenRefresher;
        private readonly IAuthenticationService _authenticationService;
        private readonly ICaptchaEvaluator _captchaEvaluator;
        private readonly OidcLoginAuditWriter _auditWriter;
        private readonly ILogger<AuthenticationFlowService> _logger;
        private readonly IRefreshTokenRepository _refreshTokenRepository;

        public AuthenticationFlowService(
            IAuthenticationRepository authenticationRepository,
            IAuthStrategy authStrategy,
            ITokenRefresher tokenRefresher,
            IAuthenticationService authenticationService,
            ICaptchaEvaluator captchaEvaluator,
            OidcLoginAuditWriter auditWriter,
            ILogger<AuthenticationFlowService> logger,
            IRefreshTokenRepository refreshTokenRepository)
        {
            _authenticationRepository = authenticationRepository;
            _authStrategy = authStrategy;
            _tokenRefresher = tokenRefresher;
            _authenticationService = authenticationService;
            _captchaEvaluator = captchaEvaluator;
            _auditWriter = auditWriter;
            _logger = logger;
            _refreshTokenRepository = refreshTokenRepository;
        }

        public async Task<AuthenticationFlowResult> ExecuteEmbeddedLoginAsync(EmbeddedLoginRequest request, HttpRequest httpRequest)
        {
            var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            if (configuration == null)
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = OAuthError.AuthConfigMissing
                };
            }

            var user = await _authenticationRepository.GetUserByUsernameAsync(request.Username);

            if (user != null
                && user.LockoutUntilUtc.HasValue
                && user.LockoutUntilUtc.Value > DateTime.UtcNow)
            {
                await _auditWriter.WriteAsync(null, null, user, httpRequest, LoginAuditEvents.LoginFailureAccountLocked, "embedded_login_account_locked");
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status423Locked,
                    Error = OAuthError.AccountLocked,
                    ErrorDescription = "Account is temporarily locked due to failed authentication attempts"
                };
            }

            if (IsEmbeddedMfaVerificationRequest(request))
            {
                return await ExecuteEmbeddedMfaVerificationAsync(request, httpRequest, configuration, user);
            }

            var captchaValidationResult = await ValidateCaptchaIfRequiredAsync(user, request.CaptchaCode);
            if (captchaValidationResult != null)
            {
                if (user != null
                    && string.Equals(captchaValidationResult.Error, OAuthError.CaptchaInvalid, StringComparison.OrdinalIgnoreCase))
                {
                    await _auditWriter.WriteAsync(null, null, user, httpRequest, LoginAuditEvents.CaptchaValidationFailure, captchaValidationResult.ErrorDescription);
                }
                return captchaValidationResult;
            }

            var resolvedOrganizationId = ResolveOrgIdFromUser(user);

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.Password,
                Username = request.Username,
                Password = request.Password,
                OrganizationId = resolvedOrganizationId,
                Request = httpRequest
            };

            var tokenResponse = await _authStrategy.AuthenticatePasswordAsync(tokenRequest, configuration);

            if (user != null && !string.IsNullOrWhiteSpace(tokenResponse.Error)
                && string.Equals(tokenResponse.Error, OAuthError.InValidUseNamePassword, StringComparison.OrdinalIgnoreCase))
            {
                await _auditWriter.WriteAsync(null, null, user, httpRequest, LoginAuditEvents.LoginFailure, tokenResponse.ErrorDescription);
            }
            else if (user != null && string.IsNullOrWhiteSpace(tokenResponse.Error))
            {
                await _auditWriter.WriteAsync(null, null, user, httpRequest, LoginAuditEvents.LoginSuccess, tokenResponse.ErrorDescription);
            }

            return new AuthenticationFlowResult
            {
                TokenResponse = tokenResponse
            };
        }

        public async Task<AuthenticationFlowResult> ExecuteSocialLoginAsync(SocialLoginRequest request, HttpRequest httpRequest)
        {
            var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            if (configuration == null)
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = OAuthError.AuthConfigMissing
                };
            }

            if (IsSocialMfaVerificationRequest(request))
            {
                return await ExecuteMfaVerificationAsync(
                    request.MfaId,
                    request.MfaCode,
                    request.MfaType,
                    httpRequest,
                    configuration);
            }

            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = "authorization_code_missing",
                    ErrorDescription = "Authorization code is required"
                };
            }

            if (string.IsNullOrWhiteSpace(request.State))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = "state_missing",
                    ErrorDescription = "State parameter is required"
                };
            }

            if (string.IsNullOrWhiteSpace(request.Provider))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = "provider_missing",
                    ErrorDescription = "Provider name is required"
                };
            }

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.Social,
                Code = request.Code,
                State = request.State,
                Request = httpRequest
            };

            return new AuthenticationFlowResult
            {
                TokenResponse = await _authStrategy.AuthenticateSocialAsync(tokenRequest, configuration)
            };
        }

        private static bool IsEmbeddedMfaVerificationRequest(EmbeddedLoginRequest request)
        {
            return !string.IsNullOrWhiteSpace(request.MfaId)
                || !string.IsNullOrWhiteSpace(request.MfaCode)
                || request.MfaType.HasValue;
        }

        private static bool IsSocialMfaVerificationRequest(SocialLoginRequest request)
        {
            return !string.IsNullOrWhiteSpace(request.MfaId)
                || !string.IsNullOrWhiteSpace(request.MfaCode)
                || request.MfaType.HasValue;
        }

        private async Task<AuthenticationFlowResult> ExecuteEmbeddedMfaVerificationAsync(
            EmbeddedLoginRequest request,
            HttpRequest httpRequest,
            IdentityConfiguration configuration,
            User? user)
        {
            return await ExecuteMfaVerificationAsync(
                request.MfaId,
                request.MfaCode,
                request.MfaType,
                httpRequest,
                configuration,
                user);
        }

        private async Task<AuthenticationFlowResult> ExecuteMfaVerificationAsync(
            string? mfaId,
            string? mfaCode,
            UserMfaType? mfaType,
            HttpRequest httpRequest,
            IdentityConfiguration configuration,
            User? user = null)
        {
            if (string.IsNullOrWhiteSpace(mfaId)
                || string.IsNullOrWhiteSpace(mfaCode)
                || !mfaType.HasValue
                || mfaType.Value == UserMfaType.None)
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = "invalid_request",
                    ErrorDescription = "mfa_id, mfa_code and mfa_type are required"
                };
            }

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.MfaCode,
                MfaId = mfaId,
                Code = mfaCode,
                MfaType = mfaType.Value,
                Request = httpRequest
            };

            return new AuthenticationFlowResult
            {
                TokenResponse = await _authStrategy.AuthenticateMfaAsync(tokenRequest, configuration, user)
            };
        }

        private async Task<AuthenticationFlowResult?> ValidateCaptchaIfRequiredAsync(User? user, string? captchaCode)
        {
            if (!CaptchaGate.IsCaptchaRequired(user))
            {
                return null;
            }

            var captchaConfiguration = await _captchaEvaluator.GetConfigurationAsync();
            if (captchaConfiguration == null || !captchaConfiguration.IsEnable)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(captchaCode))
            {
                return BuildCaptchaRequiredResult(captchaConfiguration.CaptchaKey);
            }

            var verifyCaptchaResponse = await _captchaEvaluator.VerifyAsync(captchaCode, captchaConfiguration.Provider);

            return (bool)verifyCaptchaResponse.GetType().GetProperty("Verified")!.GetValue(verifyCaptchaResponse)!
                ? null
                : BuildCaptchaInvalidResult(captchaConfiguration.CaptchaKey);
        }

        private static AuthenticationFlowResult BuildCaptchaRequiredResult(string? siteKey)
        {
            return new AuthenticationFlowResult
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Error = OAuthError.CaptchaEnabled,
                ErrorDescription = "Captcha verification is required",
                CaptchaRequired = true,
                CaptchaSiteKey = siteKey
            };
        }

        private static AuthenticationFlowResult BuildCaptchaInvalidResult(string? siteKey)
        {
            return new AuthenticationFlowResult
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Error = OAuthError.CaptchaInvalid,
                ErrorDescription = "Captcha answer is invalid. Please try again.",
                CaptchaRequired = true,
                CaptchaSiteKey = siteKey
            };
        }

        private static string ResolveOrgIdFromUser(User? user)
        {
            if (user == null)
            {
                return "default";
            }

            if (HasOrganizationAccess(user, user.LastUsedOrganizationId))
            {
                return user.LastUsedOrganizationId!;
            }

            if (HasOrganizationAccess(user, "default"))
            {
                return "default";
            }

            return user.OrganizationIds.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id))
                ?? user.Roles.Keys.FirstOrDefault(key => !string.IsNullOrWhiteSpace(key))
                ?? user.Permissions.Keys.FirstOrDefault(key => !string.IsNullOrWhiteSpace(key))
                ?? "default";
        }

        private static bool HasOrganizationAccess(User user, string? organizationId)
        {
            if (string.IsNullOrWhiteSpace(organizationId))
            {
                return false;
            }

            return user.OrganizationIds.Contains(organizationId)
                || user.Roles.ContainsKey(organizationId)
                || user.Permissions.ContainsKey(organizationId);
        }

        public async Task<AuthenticationFlowResult> ExecuteSwitchOrganizationAsync(SwitchOrganizationRequest request, ClaimsPrincipal principal, HttpRequest httpRequest)
        {
            var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            if (configuration == null)
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = OAuthError.AuthConfigMissing
                };
            }

            if (string.IsNullOrWhiteSpace(request.OrganizationId))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = "invalid_request",
                    ErrorDescription = "organization_id is required"
                };
            }

            var tenantId = principal.FindFirstValue(BlocksContext.TENANT_ID_CLAIM)
                ?? BlocksContext.GetContext()?.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Error = "tenant_not_resolved"
                };
            }

            var userId = principal.FindFirstValue(BlocksContext.USER_ID_CLAIM)
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Error = "invalid_user"
                };
            }

            var user = await _authenticationRepository.GetUserByIdAsync(userId);
            var hasOrganizationAccess = user != null
                && (
                    user.OrganizationIds.Contains(request.OrganizationId)
                    || user.Roles.ContainsKey(request.OrganizationId)
                    || user.Permissions.ContainsKey(request.OrganizationId)
                );

            if (!hasOrganizationAccess)
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = "organization_not_available"
                };
            }

            var refreshToken = _authenticationService.CookieToken(httpRequest);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Error = OAuthError.SessionExpired
                };
            }

            var refreshCacheRaw = await _tokenRefresher.GetCacheValueAsync(refreshToken);
            var refreshCache = string.IsNullOrWhiteSpace(refreshCacheRaw)
                ? null
                : JsonSerializer.Deserialize<RefreshTokenCache>(refreshCacheRaw);

            if (refreshCache == null || refreshCache.ExpiresUtc <= DateTime.UtcNow)
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Error = OAuthError.SessionExpired
                };
            }

            if (!string.Equals(refreshCache.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(refreshCache.UserId, userId, StringComparison.OrdinalIgnoreCase))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Error = OAuthError.SessionExpired
                };
            }

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.SwitchOrganization,
                OrganizationId = request.OrganizationId,
                RefreshToken = refreshToken,
                Request = httpRequest
            };

            return new AuthenticationFlowResult
            {
                TokenResponse = await _tokenRefresher.AuthenticateAsync(tokenRequest, configuration, user!)
            };
        }

        public async Task<IActionResult> ExecuteRefreshAsync(RefreshRequest request, ClaimsPrincipal principal, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            if (configuration == null)
            {
                return new BadRequestObjectResult(new { error = OAuthError.AuthConfigMissing });
            }

            var refreshToken = string.IsNullOrWhiteSpace(request.RefreshToken)
                ? _authenticationService.CookieToken(httpRequest)
                : request.RefreshToken;

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return new BadRequestObjectResult(new { error = OAuthError.InvalidRefreshToken, error_description = "Refresh token is required" });
            }

            var cachedRefreshToken = await _tokenRefresher.GetCacheValueAsync(refreshToken);

            // A cache miss is not evidence of replay: Redis only holds the sliding window, so a session
            // idle past it looks identical to a reused token. The persisted record decides which it was.
            var tokenCache = string.IsNullOrWhiteSpace(cachedRefreshToken)
                ? await RehydrateRefreshTokenAsync(refreshToken, configuration)
                : JsonSerializer.Deserialize<RefreshTokenCache>(cachedRefreshToken);

            if (tokenCache == null || string.IsNullOrWhiteSpace(tokenCache.UserId))
            {
                return new BadRequestObjectResult(new { error = OAuthError.InvalidRefreshToken, error_description = "Refresh token is invalid or expired" });
            }

            var currentTenantId = BlocksContext.GetContext()?.TenantId;

            if (!string.Equals(tokenCache.TenantId, currentTenantId, StringComparison.OrdinalIgnoreCase))
            {
                return new BadRequestObjectResult(new { error = OAuthError.InvalidRefreshToken, error_description = "Refresh token tenant mismatch" });
            }

            var user = await _authenticationRepository.GetUserByIdAsync(tokenCache.UserId);
            if (user == null)
            {
                return new UnauthorizedObjectResult(new { error = "invalid_user" });
            }

            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
            {
                return new ObjectResult(new
                {
                    error = OAuthError.AccountLocked,
                    error_description = "Account is temporarily locked due to failed authentication attempts"
                })
                {
                    StatusCode = StatusCodes.Status423Locked
                };
            }

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.RefreshToken,
                OrganizationId = string.IsNullOrWhiteSpace(tokenCache.OrganizationId) ? "default" : tokenCache.OrganizationId,
                RefreshToken = refreshToken,
                Request = httpRequest
            };

            var response = await _tokenRefresher.AuthenticateAsync(tokenRequest, configuration, user);

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

            var tenantId = BlocksContext.GetContext()?.TenantId ?? "default";
            var tenant = await _tokenRefresher.GetTenantByIDAsync(tenantId);
            var (domain, _, _) = DomainResolver.ResolveDomain(tenant, httpRequest);
            AppendCookies(response, httpResponse, domain);

            return new OkObjectResult(new
            {
                access_token = response.AccessToken,
                refresh_token = response.RefreshToken,
                token_type = response.TokenType,
                expires_in = response.ExpiresIn,
                scope = response.Scope,
                id_token = response.IdToken,
                cookie_set = true
            });
        }

        /// <summary>
        /// Recovers a refresh token that is absent from the cache. Redis only holds the sliding window,
        /// while MongoDB is the system of record and keeps the real absolute lifetime, so a session idle
        /// past the sliding window must still be able to refresh. Returns null when the token genuinely
        /// cannot be used, having first revoked it if — and only if — this looks like replay.
        /// </summary>
        private async Task<RefreshTokenCache?> RehydrateRefreshTokenAsync(string refreshToken, IdentityConfiguration configuration)
        {
            var tokenFingerprint = TruncateToken(refreshToken);
            var persisted = await _refreshTokenRepository.GetByTokenIdAsync(refreshToken);

            if (persisted == null)
            {
                _logger.LogWarning("Refresh token {TokenFingerprint} is present in neither the cache nor the store.", tokenFingerprint);
                return null;
            }

            if (persisted.IsRevoked)
            {
                // Presenting a token that was already rotated away is the only genuine reuse signal
                // available here. Every other revocation (logout, impersonation transition) is expected
                // and must not escalate into a reuse response.
                if (string.Equals(persisted.RevokeReason, SupersededByRotationReason, StringComparison.Ordinal))
                {
                    await HandlePotentialRefreshTokenReuseAsync(refreshToken);
                }
                else
                {
                    _logger.LogInformation(
                        "Refresh token {TokenFingerprint} was already revoked ({RevokeReason}).",
                        tokenFingerprint,
                        persisted.RevokeReason ?? "unspecified");
                }

                return null;
            }

            if (persisted.AbsoluteExpiry <= DateTime.UtcNow)
            {
                _logger.LogInformation("Refresh token {TokenFingerprint} is past its absolute expiry.", tokenFingerprint);
                return null;
            }

            var tokenCache = new RefreshTokenCache
            {
                RefreshToken = persisted.TokenId,
                TenantId = persisted.TenantId,
                OrganizationId = persisted.OrganizationId,
                ClientId = persisted.ClientId,
                SessionId = persisted.SessionId ?? string.Empty,
                IssuedUtc = persisted.IssuedUtc,
                ExpiresUtc = persisted.SlidingExpiry,
                AbsoluteExpiresUtc = persisted.AbsoluteExpiry,
                UserId = persisted.UserId,
                IpAddresses = persisted.IpAddress,
                Scope = persisted.Scope,
                Impersonated = persisted.Impersonated,
                ImpersonationId = persisted.ImpersonationId
            };

            var slidingMinutes = configuration.RefreshTokenValidForNumberMinutes > 0
                ? configuration.RefreshTokenValidForNumberMinutes
                : IdentityConfiguration.DefaultRefreshTokenValidForNumberMinutes;
            var remainingAbsoluteSeconds = (int)Math.Max(1, (persisted.AbsoluteExpiry - DateTime.UtcNow).TotalSeconds);
            var ttlSeconds = Math.Min(slidingMinutes * 60, remainingAbsoluteSeconds);

            // The rotation that follows reads this entry back out of the cache to supersede it, so the
            // entry has to be written here rather than merely returned to the caller.
            await _tokenRefresher.SetCacheValueAsync(refreshToken, JsonSerializer.Serialize(tokenCache), ttlSeconds);

            _logger.LogInformation(
                "Rehydrated refresh token {TokenFingerprint} from the store after a cache miss; absolute expiry {AbsoluteExpiry:o}.",
                tokenFingerprint,
                persisted.AbsoluteExpiry);

            return tokenCache;
        }

        private async Task HandlePotentialRefreshTokenReuseAsync(string refreshToken)
        {
            await _refreshTokenRepository.RevokeByTokenIdAsync(refreshToken, "potential_reuse");

            await _tokenRefresher.RemoveKeyAsync(refreshToken);
            var tokenFingerprint = TruncateToken(refreshToken);
            _logger.LogWarning("Potential refresh token reuse detected for token {TokenFingerprint}. Existing session revoked.", tokenFingerprint);
        }

        private static string TruncateToken(string token)
        {
            const int visibleLength = 8;
            return token.Length <= visibleLength ? token : string.Concat(token.AsSpan(0, visibleLength), "...");
        }

        private static bool AppendCookies(TokenResponse response, HttpResponse httpResponse, string domain)
        {
            return CookieHelper.AppendCookies(response, httpResponse, domain);
        }

        public Task<IActionResult> ExecuteImpersonateAsync(ImpersonateRequest request, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            return _authenticationService.ExecuteImpersonateAsync(request, httpRequest, httpResponse);
        }

        public Task<IActionResult> ExecuteStopImpersonationAsync(StopImpersonationRequest request, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            return _authenticationService.ExecuteStopImpersonationAsync(request, httpRequest, httpResponse);
        }
    }
}
