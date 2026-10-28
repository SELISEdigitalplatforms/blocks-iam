using Authentication.DomainService.Entities;
using Iam.DomainService.Utilities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Dtos;
using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using System.Text.Json;
using Blocks.Genesis.Entities.Iam;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Orchestrates the OIDC headless login flow:
    /// social provider redirect → MFA challenge/completion → password captcha/MFA → call <see cref="OidcAuthorizationEndpoint.AuthorizeAsync"/>.
    /// Extracted from <c>AuthorizationFlowService.ExecuteOidcLoginAsync</c> to shrink the orchestrator.
    /// </summary>
    public sealed class OidcLoginOrchestrator
    {
        private const string DefaultScope = "openid profile email offline_access";

        /// <summary>
        /// Falls back to the default scope when the caller supplies none.
        ///
        /// Deliberately whitespace-aware rather than a null check: the login form posts
        /// `scope: ""` when it has no scope in the URL, and an empty string slips past `??`
        /// only to be rejected downstream by OidcAuthRequestValidator as "scope is
        /// required". That happens whenever the login page is reached other than through a
        /// full authorize request — e.g. the "log in" link on an activation email.
        /// </summary>
        private static string ResolveScope(string? scope) =>
            string.IsNullOrWhiteSpace(scope) ? DefaultScope : scope;

        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IMfaChallengeIssuer _mfaChallengeIssuer;
        private readonly IAuthenticationService _authenticationService;
        private readonly ITenants _tenants;
        private readonly ICacheClient _cacheClient;
        private readonly IUserRepository _userRepository;
        private readonly PasswordHasher _passwordHasher;
        private readonly OidcLoginAuditWriter _auditWriter;
        private readonly OidcCaptchaEvaluator _captchaEvaluator;
        private readonly OidcAuthorizationEndpoint _authorizationEndpoint;
        private readonly ILogger<OidcLoginOrchestrator> _logger;
        private readonly IUnknownUserLoginGuard _unknownUserLoginGuard;

        private readonly IMfaAttemptGate _mfaAttemptGate;

        public OidcLoginOrchestrator(
            IAuthenticationRepository authenticationRepository,
            IMfaChallengeIssuer mfaChallengeIssuer,
            IAuthenticationService authenticationService,
            ITenants tenants,
            ICacheClient cacheClient,
            IUserRepository userRepository,
            PasswordHasher passwordHasher,
            OidcLoginAuditWriter auditWriter,
            OidcCaptchaEvaluator captchaEvaluator,
            OidcAuthorizationEndpoint authorizationEndpoint,
            ILogger<OidcLoginOrchestrator> logger,
            IUnknownUserLoginGuard unknownUserLoginGuard,
            IMfaAttemptGate? mfaAttemptGate = null)
        {
            _authenticationRepository = authenticationRepository;
            _mfaAttemptGate = mfaAttemptGate ?? new MfaAttemptGate(authenticationRepository);
            _mfaChallengeIssuer = mfaChallengeIssuer;
            _authenticationService = authenticationService;
            _tenants = tenants;
            _cacheClient = cacheClient;
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
            _auditWriter = auditWriter;
            _captchaEvaluator = captchaEvaluator;
            _authorizationEndpoint = authorizationEndpoint;
            _logger = logger;
            _unknownUserLoginGuard = unknownUserLoginGuard;
        }

        public async Task<IActionResult> ExecuteAsync(OidcLoginRequest request, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            if (!string.IsNullOrWhiteSpace(request.ProviderClientId))
            {
                return await InitiateSocialLoginAsync(request);
            }

            if (!string.IsNullOrWhiteSpace(request.MfaId) || !string.IsNullOrWhiteSpace(request.MfaCode))
            {
                return await CompleteMfaLoginAsync(request, httpRequest, httpResponse);
            }

            return await ExecutePasswordLoginAsync(request, httpRequest);
        }

        private async Task<IActionResult> InitiateSocialLoginAsync(OidcLoginRequest request)
        {
            var oidcState = Guid.NewGuid().ToString("n");
            var contextKey = $"oidc_context:{oidcState}";
            var contextValue = JsonSerializer.Serialize(new OidcContext
            {
                ClientId = request.ClientId ?? string.Empty,
                ProviderClientId = request.ProviderClientId ?? string.Empty,
                State = request.State ?? string.Empty,
                RedirectUri = request.RedirectUri ?? string.Empty,
                ProviderRedirectUri = request.ProviderRedirectUri ?? string.Empty,
                Scope = request.Scope,
                Nonce = request.Nonce,
                CodeChallenge = request.CodeChallenge,
                CodeChallengeMethod = request.CodeChallengeMethod,
                TenantId = request.TenantId,
                CreatedAt = DateTime.UtcNow
            });
            await _cacheClient.AddStringValueAsync(contextKey, contextValue, IdpConstants.OidcAuthorizationCodeCacheTtlSeconds);

            return await _authenticationService.GetOidcSocialAuthorizationUrlAsync(request.ProviderClientId, oidcState, request.ProviderRedirectUri ?? string.Empty);
        }

        private async Task<IActionResult> ExecutePasswordLoginAsync(OidcLoginRequest request, HttpRequest httpRequest)
        {
            var (inputError, user, tenant, requestedTenantId) = await ValidateInputsAsync(request);
            if (inputError != null)
            {
                return inputError;
            }

            if (user == null)
            {
                return await ExecuteUnknownUserPasswordLoginAsync(request, tenant);
            }

            var captchaResult = await _captchaEvaluator.EvaluateAsync(user!, request.CaptchaCode);
            if (captchaResult.Required)
            {
                await _auditWriter.WriteAsync(request, user!, httpRequest, captchaResult.Outcome switch
                {
                    OidcCaptchaEvaluator.CaptchaOutcome.Missing => LoginAuditEvents.CaptchaValidationFailure,
                    OidcCaptchaEvaluator.CaptchaOutcome.Invalid => LoginAuditEvents.CaptchaValidationFailure,
                    _ => LoginAuditEvents.LoginFailure
                }, LoginAuditEvents.OidcLoginCaptchaInvalid);

                return OidcCaptchaEvaluator.BuildResult(captchaResult);
            }

            // Reserve the password check atomically before BCrypt (see PasswordCredentialVerifier):
            // parallel requests share the stale user read above.
            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync() ?? new IdentityConfiguration();
            var reserved = await _authenticationRepository.TryReserveLoginAttemptAsync(
                user!.ItemId, authConfiguration.GetNumberOfWrongAttemptsToLockTheAccount, DateTime.UtcNow);
            if (reserved == null)
            {
                return await AccountLockedAsync(request, user, httpRequest);
            }

            if (!VerifyPassword(request, user, tenant))
            {
                return await HandleInvalidPasswordAsync(request, user, reserved, authConfiguration, httpRequest);
            }

            if (!await _authenticationRepository.ReleaseLoginAttemptAsync(user.ItemId, DateTime.UtcNow))
            {
                return await AccountLockedAsync(request, user, httpRequest);
            }

            if (await _mfaChallengeIssuer.IsRequiredAsync(user!))
            {
                return await StartMfaChallengeAsync(user!, request, requestedTenantId);
            }

            await ResetAuthFailureCountersAsync(user!);

            var authorizeResult = await _authorizationEndpoint.AuthorizeAsync(
                request.ClientId ?? string.Empty,
                "code",
                request.RedirectUri ?? string.Empty,
                ResolveScope(request.Scope),
                request.State ?? string.Empty,
                request.Nonce ?? string.Empty,
                request.CodeChallenge ?? string.Empty,
                request.CodeChallengeMethod ?? IdpConstants.PkceMethodS256,
                null,
                requestedTenantId ?? string.Empty,
                httpRequest.HttpContext.Request,
                httpRequest.HttpContext.Response,
                user!.ItemId,
                false,
                mfaCompleted: false);

            await _auditWriter.WriteAsync(request, user!, httpRequest, LoginAuditEvents.LoginSuccess, LoginAuditEvents.OidcLoginSuccess);

            return authorizeResult;
        }

        private async Task<(IActionResult? Error, User? User, Tenant? Tenant, string? RequestedTenantId)> ValidateInputsAsync(OidcLoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return (new BadRequestObjectResult(new { error = "invalid_request", error_description = "username and password are required" }), null, null, null);
            }

            if (string.IsNullOrWhiteSpace(request.ClientId))
            {
                return (new BadRequestObjectResult(new { error = "invalid_client", error_description = "client_id is required" }), null, null, null);
            }

            var oidcClient = await _authenticationRepository.GetOidcClientRegistrationAsync(request.ClientId);

            // Device-flow clients (RFC 8628) have no redirect_uri — this call only needs to
            // authenticate the user and establish the IdP session; the SPA already knows to
            // return to the device verification page (returnUrl) afterward. Checked ahead of the
            // "client not found" error below (same precedence as before this exemption existed)
            // so an unrelated bad client_id still surfaces as a redirect_uri error, unchanged.
            if (!(oidcClient?.IsDeviceFlowClient ?? false) && string.IsNullOrWhiteSpace(request.RedirectUri))
            {
                return (new BadRequestObjectResult(new { error = "invalid_request", error_description = "redirect_uri is required" }), null, null, null);
            }

            if (oidcClient == null)
            {
                return (new BadRequestObjectResult(new { error = "invalid_client", error_description = $"OIDC client '{request.ClientId}' not found or not configured" }), null, null, null);
            }

            var user = await _authenticationRepository.GetUserByUsernameAsync(request.Username);
            var tenant = _tenants.GetTenantByID(request.TenantId);

            // No usable account (unknown, pending, disabled): no error here, a null user. The
            // caller answers it like a wrong password on a real account.
            if (user == null || !user.Active || !user.IsVerified)
            {
                return (null, null, tenant, request.TenantId);
            }

            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
            {
                return (LockedResult(), null, null, null);
            }

            return (null, user, tenant, request.TenantId);
        }

        /// <summary>
        /// The password leg for a name with no usable account, step for step like a real account
        /// with a wrong password: lock check, captcha gate, a counted attempt with a BCrypt check,
        /// then 423 when this attempt locked it (or found no free slot), otherwise 401.
        /// </summary>
        private async Task<IActionResult> ExecuteUnknownUserPasswordLoginAsync(OidcLoginRequest request, Tenant? tenant)
        {
            var state = await _unknownUserLoginGuard.GetStateAsync(request.Username);
            if (state.Locked)
            {
                return LockedResult();
            }

            var captchaResult = await _captchaEvaluator.EvaluateAsync(state.FailedCount, request.CaptchaCode);
            if (captchaResult.Required)
            {
                return OidcCaptchaEvaluator.BuildResult(captchaResult);
            }

            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync() ?? new IdentityConfiguration();
            var attempt = await _unknownUserLoginGuard.FailAsync(request.Username, request.Password, tenant?.TenantSalt, authConfiguration);
            return !attempt.Reserved || attempt.LockedNow
                ? LockedResult()
                : new UnauthorizedObjectResult(new { error = "invalid_credentials" });
        }

        private static IActionResult LockedResult() =>
            new ObjectResult(new { error = "account_locked" }) { StatusCode = StatusCodes.Status423Locked };

        private bool VerifyPassword(OidcLoginRequest request, User user, Tenant? tenant)
        {
            try
            {
                return _passwordHasher.Verify(request.Password, user.Password ?? string.Empty, tenant?.TenantSalt);
            }
            catch
            {
                return false;
            }
        }

        private async Task<IActionResult> HandleInvalidPasswordAsync(
            OidcLoginRequest request, User user, User reserved, IdentityConfiguration authConfiguration, HttpRequest httpRequest)
        {
            // The attempt is already counted by the reservation; only the one that took the last slot can lock.
            var threshold = authConfiguration.GetNumberOfWrongAttemptsToLockTheAccount;
            var lockedUser = reserved.FailedLoginCount >= threshold
                ? await _authenticationRepository.LockAccountIfLoginThresholdReachedAsync(
                    user.ItemId,
                    threshold,
                    authConfiguration.AccountLockDurationInMinutes,
                    DateTime.UtcNow)
                : null;

            if (lockedUser != null)
            {
                return await AccountLockedAsync(request, user, httpRequest);
            }

            await _auditWriter.WriteAsync(request, user, httpRequest, LoginAuditEvents.LoginFailure, LoginAuditEvents.OidcLoginFailure);
            return new UnauthorizedObjectResult(new { error = "invalid_credentials" });
        }

        private async Task<IActionResult> AccountLockedAsync(OidcLoginRequest request, User user, HttpRequest httpRequest)
        {
            await _auditWriter.WriteAsync(request, user, httpRequest, LoginAuditEvents.LoginFailureAccountLocked, LoginAuditEvents.OidcLoginAccountLocked);
            return LockedResult();
        }

        private async Task<IActionResult> StartMfaChallengeAsync(User user, OidcLoginRequest request, string? tenantId)
        {
            var otpService = await _mfaChallengeIssuer.GetOtpServiceAsync(user);
            if (otpService == null)
            {
                return new ObjectResult(new { error = "server_error", error_description = "Mfa provider is not available" })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }

            var challengeResponse = await otpService.GenerateAsync(new UserInfo
            {
                ItemId = user.ItemId,
                Email = user.Email,
                Language = user.Language ?? "en-US",
                PhoneNumber = user.PhoneNumber
            });

            if (challengeResponse == null || !challengeResponse.IsSuccess || string.IsNullOrWhiteSpace(challengeResponse.MfaId))
            {
                return new ObjectResult(new { error = "server_error", error_description = "Failed to generate mfa challenge" })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }

            var mfaContext = new OidcMfaLoginContext
            {
                UserId = user.ItemId,
                ClientId = request.ClientId ?? string.Empty,
                RedirectUri = request.RedirectUri ?? string.Empty,
                Scope = ResolveScope(request.Scope),
                State = request.State ?? string.Empty,
                Nonce = request.Nonce ?? string.Empty,
                CodeChallenge = request.CodeChallenge ?? string.Empty,
                CodeChallengeMethod = request.CodeChallengeMethod ?? IdpConstants.PkceMethodS256,
                TenantId = tenantId ?? string.Empty
            };

            await _cacheClient.AddStringValueAsync(
                $"oidc_mfa_login:{challengeResponse.MfaId}",
                JsonSerializer.Serialize(mfaContext),
                IdpConstants.OidcStateCacheTtlSeconds);

            return new OkObjectResult(new
            {
                error = OAuthError.MfaEnabled,
                error_description = "Mfa code required",
                mfa_id = challengeResponse.MfaId,
                user_mfa = user.UserMfaType.ToString()
            });
        }

        private async Task<IActionResult> CompleteMfaLoginAsync(OidcLoginRequest request, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            if (string.IsNullOrWhiteSpace(request.MfaId) || string.IsNullOrWhiteSpace(request.MfaCode))
            {
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "mfa_id and mfa_code are required" });
            }

            var contextRaw = await _cacheClient.GetStringValueAsync($"oidc_mfa_login:{request.MfaId}");
            if (string.IsNullOrWhiteSpace(contextRaw))
            {
                return new BadRequestObjectResult(new { error = "invalid_mfa_session", error_description = "Mfa login session is expired or invalid" });
            }

            var mfaContext = JsonSerializer.Deserialize<OidcMfaLoginContext>(contextRaw);
            if (mfaContext == null || string.IsNullOrWhiteSpace(mfaContext.UserId))
            {
                return new BadRequestObjectResult(new { error = "invalid_mfa_session", error_description = "Mfa login session is invalid" });
            }

            var user = await _authenticationRepository.GetUserByIdAsync(mfaContext.UserId);
            if (user == null || !user.Active || !user.IsVerified)
            {
                return new UnauthorizedObjectResult(new { error = "invalid_credentials" });
            }

            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
            {
                return new ObjectResult(new { error = "account_locked" }) { StatusCode = StatusCodes.Status423Locked };
            }

            var otpService = await _mfaChallengeIssuer.GetOtpServiceAsync(user);
            if (otpService == null)
            {
                return new ObjectResult(new { error = "server_error", error_description = "Mfa provider is not available" })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }

            // Counted before it is checked, so parallel codes get no more checks than the lock threshold.
            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync() ?? new IdentityConfiguration();
            var attempt = await _mfaAttemptGate.CheckAsync(user.ItemId, authConfiguration, async () =>
                (await otpService.VerifyAsync(new VerifyOtpRequest
                {
                    AuthType = user.UserMfaType,
                    MfaId = request.MfaId,
                    VerificationCode = request.MfaCode
                })).IsValid);

            if (!attempt.IsValid)
            {
                await _mfaChallengeIssuer.WriteAuditAsync(new MfaAuditEvent
                {
                    EventType = attempt.IsLocked
                        ? LoginAuditEvents.MfaAccountLocked
                        : LoginAuditEvents.MfaVerificationFailure,
                    UserId = user.ItemId,
                    ClientId = request.ClientId,
                    MfaType = user.UserMfaType,
                    Severity = IdpConstants.SeverityWarn,
                    Status = IdpConstants.StatusFailure
                });

                if (attempt.IsLocked)
                {
                    return new ObjectResult(new { error = "account_locked" }) { StatusCode = StatusCodes.Status423Locked };
                }

                return new UnauthorizedObjectResult(new { error = "invalid_mfa_code" });
            }

            await ResetAuthFailureCountersAsync(user);
            await _cacheClient.RemoveKeyAsync($"oidc_mfa_login:{request.MfaId}");

            await _mfaChallengeIssuer.WriteAuditAsync(new MfaAuditEvent
            {
                EventType = LoginAuditEvents.MfaVerificationSuccess,
                UserId = user.ItemId,
                ClientId = request.ClientId,
                MfaType = user.UserMfaType,
                Status = IdpConstants.StatusSuccess
            });

            var authorizeResult = await _authorizationEndpoint.AuthorizeAsync(
                mfaContext.ClientId ?? string.Empty,
                "code",
                mfaContext.RedirectUri ?? string.Empty,
                ResolveScope(mfaContext.Scope),
                mfaContext.State ?? string.Empty,
                mfaContext.Nonce ?? string.Empty,
                mfaContext.CodeChallenge ?? string.Empty,
                mfaContext.CodeChallengeMethod ?? IdpConstants.PkceMethodS256,
                null,
                mfaContext.TenantId ?? string.Empty,
                httpRequest,
                httpResponse,
                user.ItemId,
                false,
                mfaCompleted: true);

            await _auditWriter.WriteAsync(mfaContext.TenantId, mfaContext.ClientId, user, httpRequest, LoginAuditEvents.LoginSuccess, LoginAuditEvents.OidcLoginSuccess);

            return authorizeResult;
        }

        private async Task ResetAuthFailureCountersAsync(User user)
        {
            // Atomic and conditional: never clears an active lock, and never forgives a wrong password counted
            // after this sign-in read the user.
            await _authenticationRepository.ResetFailureCountersAfterSuccessAsync(user, DateTime.UtcNow).ConfigureAwait(false);
        }

        internal sealed class OidcMfaLoginContext
        {
            public string UserId { get; set; } = string.Empty;
            public string ClientId { get; set; } = string.Empty;
            public string RedirectUri { get; set; } = string.Empty;
            public string Scope { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
            public string Nonce { get; set; } = string.Empty;
            public string CodeChallenge { get; set; } = string.Empty;
            public string CodeChallengeMethod { get; set; } = IdpConstants.PkceMethodS256;
            public string TenantId { get; set; } = string.Empty;
        }
    }
}
