using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.Dtos;
using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using System.Text.Json;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Orchestrates the OIDC headless login flow:
    /// social provider redirect → MFA challenge/completion → password captcha/MFA → call <see cref="OidcAuthorizationEndpoint.AuthorizeAsync"/>.
    /// Extracted from <c>AuthorizationFlowService.ExecuteOidcLoginAsync</c> to shrink the orchestrator.
    /// </summary>
    public sealed class OidcLoginOrchestrator
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IAuditLogRepository _auditLogRepo;
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

        public OidcLoginOrchestrator(
            IAuthenticationRepository authenticationRepository,
            IAuditLogRepository auditLogRepo,
            IMfaChallengeIssuer mfaChallengeIssuer,
            IAuthenticationService authenticationService,
            ITenants tenants,
            ICacheClient cacheClient,
            IUserRepository userRepository,
            PasswordHasher passwordHasher,
            OidcLoginAuditWriter auditWriter,
            OidcCaptchaEvaluator captchaEvaluator,
            OidcAuthorizationEndpoint authorizationEndpoint,
            ILogger<OidcLoginOrchestrator> logger)
        {
            _authenticationRepository = authenticationRepository;
            _auditLogRepo = auditLogRepo;
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
            await _cacheClient.AddStringValueAsync(contextKey, contextValue, AuthenticationConstants.OidcAuthorizationCodeCacheTtlSeconds);

            return await _authenticationService.GetOidcSocialAuthorizationUrlAsync(request.ProviderClientId, oidcState, request.ProviderRedirectUri ?? string.Empty);
        }

        private async Task<IActionResult> ExecutePasswordLoginAsync(OidcLoginRequest request, HttpRequest httpRequest)
        {
            var (inputError, user, tenant, requestedTenantId) = await ValidateInputsAsync(request);
            if (inputError != null)
            {
                return inputError;
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

            if (!VerifyPassword(request, user!, tenant))
            {
                return await HandleInvalidPasswordAsync(request, user!, httpRequest);
            }

            if (await _mfaChallengeIssuer.IsRequiredAsync(user!))
            {
                return await StartMfaChallengeAsync(user!, request, requestedTenantId);
            }

            await ResetAuthFailureCountersAsync(user!);
            await _auditWriter.WriteAsync(request, user!, httpRequest, LoginAuditEvents.LoginSuccess, LoginAuditEvents.OidcLoginSuccess);

            return await _authorizationEndpoint.AuthorizeAsync(
                request.ClientId ?? string.Empty,
                "code",
                request.RedirectUri ?? string.Empty,
                request.Scope ?? "openid profile email offline_access",
                request.State ?? string.Empty,
                request.Nonce ?? string.Empty,
                request.CodeChallenge ?? string.Empty,
                request.CodeChallengeMethod ?? AuthenticationConstants.PkceMethodS256,
                null,
                requestedTenantId ?? string.Empty,
                httpRequest.HttpContext.Request,
                httpRequest.HttpContext.Response,
                user!.ItemId,
                false,
                mfaCompleted: false);
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

            if (string.IsNullOrWhiteSpace(request.RedirectUri))
            {
                return (new BadRequestObjectResult(new { error = "invalid_request", error_description = "redirect_uri is required" }), null, null, null);
            }

            if (!await HasOidcClientConfigurationAsync(request.ClientId))
            {
                return (new BadRequestObjectResult(new { error = "invalid_client", error_description = $"OIDC client '{request.ClientId}' not found or not configured" }), null, null, null);
            }

            var user = await _authenticationRepository.GetUserByUsernameAsync(request.Username);
            var tenant = _tenants.GetTenantByID(request.TenantId);

            if (user == null || !user.Active || !user.IsVerified)
            {
                return (new UnauthorizedObjectResult(new { error = "invalid_credentials" }), null, null, null);
            }

            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
            {
                return (new ObjectResult(new { error = "account_locked" }) { StatusCode = StatusCodes.Status423Locked }, null, null, null);
            }

            return (null, user, tenant, request.TenantId);
        }

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

        private async Task<IActionResult> HandleInvalidPasswordAsync(OidcLoginRequest request, User user, HttpRequest httpRequest)
        {
            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync() ?? new IdentityConfiguration();
            var updatedUser = await _authenticationRepository.IncrementFailedLoginAndApplyLockoutAsync(
                user.ItemId,
                authConfiguration.GetNumberOfWrongAttemptsToLockTheAccount,
                authConfiguration.AccountLockDurationInMinutes,
                DateTime.UtcNow);

            var accountLocked = updatedUser?.LockoutUntilUtc.HasValue == true && updatedUser.LockoutUntilUtc.Value > DateTime.UtcNow;
            if (accountLocked)
            {
                await _auditWriter.WriteAsync(request, user, httpRequest, LoginAuditEvents.LoginFailureAccountLocked, LoginAuditEvents.OidcLoginAccountLocked);
                return new ObjectResult(new { error = "account_locked" }) { StatusCode = StatusCodes.Status423Locked };
            }

            await _auditWriter.WriteAsync(request, user, httpRequest, LoginAuditEvents.LoginFailure, LoginAuditEvents.OidcLoginFailure);
            return new UnauthorizedObjectResult(new { error = "invalid_credentials" });
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
                Scope = request.Scope ?? "openid profile email offline_access",
                State = request.State ?? string.Empty,
                Nonce = request.Nonce ?? string.Empty,
                CodeChallenge = request.CodeChallenge ?? string.Empty,
                CodeChallengeMethod = request.CodeChallengeMethod ?? AuthenticationConstants.PkceMethodS256,
                TenantId = tenantId ?? string.Empty
            };

            await _cacheClient.AddStringValueAsync(
                $"oidc_mfa_login:{challengeResponse.MfaId}",
                JsonSerializer.Serialize(mfaContext),
                AuthenticationConstants.OidcStateCacheTtlSeconds);

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

            var verificationResponse = await otpService.VerifyAsync(new VerifyOtpRequest
            {
                AuthType = user.UserMfaType,
                MfaId = request.MfaId,
                VerificationCode = request.MfaCode
            });

            if (!verificationResponse.IsValid)
            {
                var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync() ?? new IdentityConfiguration();
                var updatedUser = await _authenticationRepository.IncrementFailedMfaAndApplyLockoutAsync(
                    user.ItemId,
                    authConfiguration.GetNumberOfWrongAttemptsToLockTheAccount,
                    authConfiguration.AccountLockDurationInMinutes,
                    DateTime.UtcNow);

                await _mfaChallengeIssuer.WriteAuditAsync(new MfaAuditEvent
                {
                    EventType = updatedUser?.LockoutUntilUtc.HasValue == true && updatedUser.LockoutUntilUtc.Value > DateTime.UtcNow
                        ? LoginAuditEvents.MfaAccountLocked
                        : LoginAuditEvents.MfaVerificationFailure,
                    UserId = user.ItemId,
                    ClientId = request.ClientId,
                    MfaType = user.UserMfaType,
                    Severity = AuthenticationConstants.SeverityWarn,
                    Status = AuthenticationConstants.StatusFailure
                });

                if (updatedUser?.LockoutUntilUtc.HasValue == true && updatedUser.LockoutUntilUtc.Value > DateTime.UtcNow)
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
                Status = AuthenticationConstants.StatusSuccess
            });

            return await _authorizationEndpoint.AuthorizeAsync(
                mfaContext.ClientId ?? string.Empty,
                "code",
                mfaContext.RedirectUri ?? string.Empty,
                mfaContext.Scope ?? "openid profile email offline_access",
                mfaContext.State ?? string.Empty,
                mfaContext.Nonce ?? string.Empty,
                mfaContext.CodeChallenge ?? string.Empty,
                mfaContext.CodeChallengeMethod ?? AuthenticationConstants.PkceMethodS256,
                null,
                mfaContext.TenantId ?? string.Empty,
                httpRequest,
                httpResponse,
                user.ItemId,
                false,
                mfaCompleted: true);
        }

        private async Task ResetAuthFailureCountersAsync(User user)
        {
            if (user.FailedLoginCount <= 0
                && !user.LastFailedLoginUtc.HasValue
                && user.FailedMfaCount <= 0
                && !user.LastFailedMfaUtc.HasValue
                && !user.LockoutUntilUtc.HasValue)
            {
                return;
            }

            await _authenticationRepository.UpdatePartialAsync<User>(
                user.ItemId,
                new Dictionary<string, object>
                {
                    { nameof(User.FailedLoginCount), 0 },
                    { nameof(User.LastFailedLoginUtc), null! },
                    { nameof(User.FailedMfaCount), 0 },
                    { nameof(User.LastFailedMfaUtc), null! },
                    { nameof(User.LockoutUntilUtc), null! },
                    { nameof(User.LockoutCount), 0 },
                    { nameof(User.LastUpdatedDate), DateTime.UtcNow },
                    { nameof(User.LastUpdatedBy), user.ItemId }
                });
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

        internal sealed class OidcMfaLoginContext
        {
            public string UserId { get; set; } = string.Empty;
            public string ClientId { get; set; } = string.Empty;
            public string RedirectUri { get; set; } = string.Empty;
            public string Scope { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
            public string Nonce { get; set; } = string.Empty;
            public string CodeChallenge { get; set; } = string.Empty;
            public string CodeChallengeMethod { get; set; } = AuthenticationConstants.PkceMethodS256;
            public string TenantId { get; set; } = string.Empty;
        }
    }
}
