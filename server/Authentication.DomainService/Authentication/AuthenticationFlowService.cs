using Authentication.DomainService.Dtos;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using Iam.DomainService.Utilities;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Security.Claims;

namespace Authentication.DomainService.Authentication
{
    public sealed class AuthenticationFlowService : IAuthenticationFlowService
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IAuthStrategy _authStrategy;
        private readonly ITokenRefresher _tokenRefresher;
        private readonly IAuthenticationService _authenticationService;
        private readonly ICaptchaEvaluator _captchaEvaluator;
        private readonly OidcLoginAuditWriter _auditWriter;
        private readonly ILogger<AuthenticationFlowService> _logger;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IRefreshSessionResolver _refreshSessionResolver;

        public AuthenticationFlowService(
            IAuthenticationRepository authenticationRepository,
            IAuthStrategy authStrategy,
            ITokenRefresher tokenRefresher,
            IAuthenticationService authenticationService,
            ICaptchaEvaluator captchaEvaluator,
            OidcLoginAuditWriter auditWriter,
            ILogger<AuthenticationFlowService> logger,
            IRefreshTokenRepository refreshTokenRepository,
            IRefreshSessionResolver refreshSessionResolver)
        {
            _authenticationRepository = authenticationRepository;
            _authStrategy = authStrategy;
            _tokenRefresher = tokenRefresher;
            _authenticationService = authenticationService;
            _captchaEvaluator = captchaEvaluator;
            _auditWriter = auditWriter;
            _logger = logger;
            _refreshTokenRepository = refreshTokenRepository;
            _refreshSessionResolver = refreshSessionResolver;
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

            // MFA verification is identified solely by the mfa_id/mfa_code, and the account is
            // resolved from that verified mfa session downstream — never from a request-body
            // username. Handle it before any username lookup so a caller cannot pair a valid
            // mfa_id/code of their own with a different account's username and mint that
            // account's tokens. The mfa session user's own lockout is still enforced inside
            // MfaAuthorizationService after the mfa_id is verified.
            if (IsEmbeddedMfaVerificationRequest(request))
            {
                return await ExecuteMfaVerificationAsync(
                    request.MfaId,
                    request.MfaCode,
                    request.MfaType,
                    httpRequest,
                    configuration);
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
            // No request-supplied organization on this surface: EmbeddedLoginRequest has no such
            // field, so the sign-in organization is derived from the user alone. Shared with the
            // password, social and mfa legs so every one of them scopes the token identically.
            return user == null
                ? IdpConstants.DefaultOrganizationId
                : OrganizationAccessResolver.ResolveSignInOrganizationId(user, null);
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

            // The same resolver the refresh grant uses, rather than a bare cache read: Redis first, then
            // the persisted store. A cache miss is not evidence of an invalid session -- an eviction or a
            // Redis restart drops the entry while the session is still live in the store -- and switching
            // organization was the only rotation grant that failed closed on one. Resolving here also
            // applies the absolute cap, which the raw read skipped, so a session past its lifetime can no
            // longer switch organization while the sliding window happens to still be open. On a store
            // hit the resolver rehydrates the cache, so the rotation further down finds its entry too.
            var refreshCache = await _refreshSessionResolver.TryResolveRefreshSessionAsync(refreshToken, configuration);

            if (refreshCache == null)
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

            // A resolved id that differs from the presented one is a grace-window replay. The refresh
            // grant hands the successor straight back, but that is only safe when the organization is not
            // changing: a replay short-circuits ManageRefreshTokenAsync before CreateOrRotateRefreshToken
            // writes tokenRequest.OrganizationId onto the token, so the access token would carry the new
            // organization while the refresh token stayed bound to the old one -- and the next refresh
            // would silently revert the switch. Failing here instead costs one retry: the client refreshes,
            // obtains the current token and switches with it.
            if (!string.Equals(refreshCache.RefreshToken, refreshToken, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Switch organization rejected a grace-window replay for user {UserId}: the presented refresh token has been rotated away. The client should refresh and retry.",
                    userId);

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

            var tokenResponse = await _tokenRefresher.AuthenticateAsync(tokenRequest, configuration, user!);

            // The organization the user just switched into is the one their next sign-in should land
            // on. Every other organization-selecting leg persists this — password, social, mfa and the
            // oidc authorize endpoint — but switch-org, whose entire purpose is choosing an
            // organization, did not: a logout and login silently reverted the user to whichever
            // organization they last signed in through.
            if (tokenResponse != null
                && string.IsNullOrWhiteSpace(tokenResponse.Error)
                && !string.Equals(user!.LastUsedOrganizationId, request.OrganizationId, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await _authenticationRepository.UpdatePartialAsync<User>(
                        user.ItemId,
                        new Dictionary<string, object>
                        {
                            { nameof(User.LastUsedOrganizationId), request.OrganizationId },
                            { nameof(User.LastUpdatedDate), DateTime.UtcNow },
                            { nameof(User.LastUpdatedBy), user.ItemId }
                        });
                }
                catch (Exception ex)
                {
                    // Stickiness is a convenience, and the switch itself has already succeeded — the
                    // caller is about to receive tokens scoped to the new organization. Failing the
                    // response over this write would discard a completed switch to avoid landing on
                    // the old organization at the next login, which is the worse of the two outcomes.
                    _logger.LogWarning(ex,
                        "Failed to persist last used organization {OrganizationId} for user {UserId} after switch",
                        request.OrganizationId, user.ItemId);
                }
            }

            return new AuthenticationFlowResult
            {
                TokenResponse = tokenResponse
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

            // One shared check decides validity: unrevoked, inside the sliding window and inside the
            // absolute cap. A cache miss is not evidence of replay, and a token rotated away moments ago
            // resolves to its successor rather than failing.
            var tokenCache = await _refreshSessionResolver.TryResolveRefreshSessionAsync(refreshToken, configuration);

            if (tokenCache == null || string.IsNullOrWhiteSpace(tokenCache.UserId))
            {
                // The browser must stop retrying a credential that can never work again.
                ClearSessionCookies(httpRequest, httpResponse);
                return new BadRequestObjectResult(new { error = OAuthError.InvalidRefreshToken, error_description = "Refresh token is invalid or expired" });
            }

            // A resolved token id that differs from the presented one is a grace-window replay.
            var graceReplayTokenId = string.Equals(tokenCache.RefreshToken, refreshToken, StringComparison.Ordinal)
                ? null
                : tokenCache.RefreshToken;

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
                Request = httpRequest,
                GraceReplayTokenId = graceReplayTokenId,
                GraceReplayAbsoluteExpiry = graceReplayTokenId == null ? null : tokenCache.AbsoluteExpiresUtc
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
        /// Clears the access and refresh cookies after an unresolvable refresh, so the browser stops
        /// replaying a permanently dead credential on every subsequent 401.
        /// </summary>
        private void ClearSessionCookies(HttpRequest httpRequest, HttpResponse httpResponse)
        {
            try
            {
                var tenantId = BlocksContext.GetContext()?.TenantId ?? "default";
                var tenant = _tokenRefresher.GetTenantByIDAsync(tenantId).GetAwaiter().GetResult();
                var (domain, cookieDomain, isResolved) = DomainResolver.ResolveDomain(tenant, httpRequest);
                if (!isResolved || string.IsNullOrWhiteSpace(domain))
                {
                    return;
                }

                var cookieOptions = DomainResolver.CreateCookieOptions(cookieDomain, DateTime.UtcNow.AddDays(-1));
                httpResponse.Cookies.Delete($"{domain}", cookieOptions);
                httpResponse.Cookies.Delete($"{IdpConstants.RefreshTokenCookieName}_{domain}", cookieOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear session cookies after an unresolvable refresh token.");
            }
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
