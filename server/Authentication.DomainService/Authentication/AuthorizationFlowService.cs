using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Validation;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Utilities;
using Blocks.CaptchaDriver;
using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BCryptNet = BCrypt.Net.BCrypt;

namespace Authentication.DomainService.Authentication
{
    public sealed class AuthorizationFlowService : IAuthorizationFlowService
    {
        private readonly IAuthorizationCodeRepository _authCodeRepo;
        private readonly IRefreshTokenRepository _refreshTokenRepo;
        private readonly IIdpSessionRepository _sessionRepo;
        private readonly IAuditLogRepository _auditLogRepo;
        private readonly ITokenGenerationService _tokenService;
        private readonly IPkceService _pkceService;
        private readonly AuthorizeRequestValidator _authorizeValidator;
        private readonly IUserRepository _userRepository;
        private readonly IAuthorizationClaimsResolver _authorizationClaimsResolver;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ClientCredentialAuthorizationService _clientCredentialAuthorizationService;
        private readonly RefreshTokenAuthenticationService _refreshTokenAuthenticationService;
        private readonly IMfaConfigurationService _mfaConfigurationService;
        private readonly IMfaPolicyService _mfaPolicyService;
        private readonly IMfaAuditService _mfaAuditService;
        private readonly IOtpServiceFactory _otpServiceFactory;
        private readonly ITenants _tenants;
        private readonly IAuthenticationService _authenticationService;
        private readonly ICacheClient _cacheClient;
        private readonly ICaptchaService _captchaService;
        private readonly ICaptchaConfigurationService _captchaConfigurationService;
        private readonly ILogger<AuthorizationFlowService> _logger;

        public AuthorizationFlowService(
            IAuthorizationCodeRepository authCodeRepo,
            IRefreshTokenRepository refreshTokenRepo,
            IIdpSessionRepository sessionRepo,
            IAuditLogRepository auditLogRepo,
            ITokenGenerationService tokenService,
            IPkceService pkceService,
            AuthorizeRequestValidator authorizeValidator,
            IUserRepository userRepository,
            IAuthorizationClaimsResolver authorizationClaimsResolver,
            IAuthenticationRepository authenticationRepository,
            ClientCredentialAuthorizationService clientCredentialAuthorizationService,
            RefreshTokenAuthenticationService refreshTokenAuthenticationService,
            IMfaConfigurationService mfaConfigurationService,
            IMfaPolicyService mfaPolicyService,
            IMfaAuditService mfaAuditService,
            IOtpServiceFactory otpServiceFactory,
            ITenants tenants,
            IAuthenticationService authenticationService,
            ICacheClient cacheClient,
            ICaptchaService captchaService,
            ICaptchaConfigurationService captchaConfigurationService,
            ILogger<AuthorizationFlowService> logger)
        {
            _authCodeRepo = authCodeRepo;
            _refreshTokenRepo = refreshTokenRepo;
            _sessionRepo = sessionRepo;
            _auditLogRepo = auditLogRepo;
            _tokenService = tokenService;
            _pkceService = pkceService;
            _authorizeValidator = authorizeValidator;
            _userRepository = userRepository;
            _authorizationClaimsResolver = authorizationClaimsResolver;
            _authenticationRepository = authenticationRepository;
            _clientCredentialAuthorizationService = clientCredentialAuthorizationService;
            _refreshTokenAuthenticationService = refreshTokenAuthenticationService;
            _mfaConfigurationService = mfaConfigurationService;
            _mfaPolicyService = mfaPolicyService;
            _mfaAuditService = mfaAuditService;
            _otpServiceFactory = otpServiceFactory;
            _tenants = tenants;
            _authenticationService = authenticationService;
            _cacheClient = cacheClient;
            _captchaService = captchaService;
            _captchaConfigurationService = captchaConfigurationService;
            _logger = logger;
        }

        public async Task<IActionResult> ExecuteOidcLoginAsync(OidcLoginRequest request, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            // If provider is specified, initiate social authentication flow
            if (!string.IsNullOrWhiteSpace(request.ProviderClientId))
            {
                var oidcState = Guid.NewGuid().ToString("n");

                var contextKey = $"oidc_context:{oidcState}";
                var contextValue = JsonSerializer.Serialize(new
                {
                    clientId = request.ClientId,
                    providerClientId = request.ProviderClientId,
                    state = request.State,
                    redirectUri = request.RedirectUri,
                    providerRedirectUri = request.ProviderRedirectUri,
                    scope = request.Scope,
                    nonce = request.Nonce,
                    codeChallenge = request.CodeChallenge,
                    codeChallengeMethod = request.CodeChallengeMethod,
                    tenantId = request.TenantId,
                    createdAt = DateTime.UtcNow
                });
                await _cacheClient.AddStringValueAsync(contextKey, contextValue, 600); // 10 minute TTL

                // Get social authorization URL
                return await _authenticationService.GetOidcSocialAuthorizationUrlAsync(request.ProviderClientId, oidcState, request.ProviderRedirectUri ?? string.Empty);
            }

            if (!string.IsNullOrWhiteSpace(request.MfaId) || !string.IsNullOrWhiteSpace(request.MfaCode))
            {
                return await CompleteOidcMfaLoginAsync(request, httpRequest, httpResponse);
            }

            // Standard password-based OIDC login flow
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "username and password are required" });

            if (string.IsNullOrWhiteSpace(request.ClientId))
                return new BadRequestObjectResult(new { error = "invalid_client", error_description = "client_id is required" });

            if (string.IsNullOrWhiteSpace(request.RedirectUri))
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "redirect_uri is required" });

            var requestedTenantId = request.TenantId;

            // Validate client exists
            if (!await HasOidcClientConfigurationAsync(request.ClientId))
                return new BadRequestObjectResult(new { error = "invalid_client", error_description = $"OIDC client '{request.ClientId}' not found or not configured" });

            // Look up user and verify credentials (no tenant scoping on initial lookup, like embedded login)
            var user = await _authenticationRepository.GetUserByUsernameAsync(request.Username);
            var tenant = _tenants.GetTenantByID(requestedTenantId);
            if (user == null || !user.Active || !user.IsVerified)
                return new UnauthorizedObjectResult(new { error = "invalid_credentials" });

            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
                return new ObjectResult(new { error = "account_locked" }) { StatusCode = 423 };

            var captcha = await EvaluateOidcCaptchaAsync(user, request.CaptchaCode);
            if (captcha.Required)
            {
                await WriteOidcLoginAuditAsync(request, user, httpRequest, captcha.Outcome switch
                {
                    CaptchaOutcome.Missing => LoginAuditEvents.CaptchaValidationFailure,
                    CaptchaOutcome.Invalid => LoginAuditEvents.CaptchaValidationFailure,
                    _ => LoginAuditEvents.LoginFailure
                }, "oidc_login_captcha_invalid");
                return BuildCaptchaResult(captcha);
            }

            bool passwordValid;
            try
            {
                passwordValid = VerifyPassword(request.Password, user.Password ?? string.Empty, tenant?.TenantSalt);
            }
            catch
            {
                passwordValid = false;
            }

            if (!passwordValid)
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
                    await WriteOidcLoginAuditAsync(request, user, httpRequest, LoginAuditEvents.LoginFailureAccountLocked, "oidc_login_account_locked");
                    return new ObjectResult(new { error = "account_locked" }) { StatusCode = StatusCodes.Status423Locked };
                }

                await WriteOidcLoginAuditAsync(request, user, httpRequest, LoginAuditEvents.LoginFailure, "oidc_login_invalid_credentials");
                return new UnauthorizedObjectResult(new { error = "invalid_credentials" });
            }

            if (await IsMfaRequiredAsync(user))
            {
                return await StartOidcMfaChallengeAsync(user, request, requestedTenantId);
            }

            await ResetAuthFailureCountersAsync(user);
            await WriteOidcLoginAuditAsync(request, user, httpRequest, LoginAuditEvents.LoginSuccess, "oidc_login_success");

            return await AuthorizeAsync(
                request.ClientId ?? string.Empty,
                "code",
                request.RedirectUri ?? string.Empty,
                request.Scope ?? "openid profile email offline_access",
                request.State ?? string.Empty,
                request.Nonce ?? string.Empty,
                request.CodeChallenge ?? string.Empty,
                request.CodeChallengeMethod ?? "S256",
                null,
                requestedTenantId ?? string.Empty,
                httpRequest,
                httpResponse,
                user.ItemId,
                false,
                mfaCompleted: false);
        }

        private async Task<IActionResult> CompleteOidcMfaLoginAsync(OidcLoginRequest request, HttpRequest httpRequest, HttpResponse httpResponse)
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

            var otpService = _otpServiceFactory.GetOTPService(user.UserMfaType);
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

                await _mfaAuditService.WriteAsync(new MfaAuditEvent
                {
                    EventType = updatedUser?.LockoutUntilUtc.HasValue == true && updatedUser.LockoutUntilUtc.Value > DateTime.UtcNow
                        ? LoginAuditEvents.MfaAccountLocked
                        : LoginAuditEvents.MfaVerificationFailure,
                    UserId = user.ItemId,
                    ClientId = request.ClientId,
                    MfaType = user.UserMfaType,
                    Severity = "WARN",
                    Status = "failure"
                });

                if (updatedUser?.LockoutUntilUtc.HasValue == true && updatedUser.LockoutUntilUtc.Value > DateTime.UtcNow)
                {
                    return new ObjectResult(new { error = "account_locked" }) { StatusCode = StatusCodes.Status423Locked };
                }

                return new UnauthorizedObjectResult(new { error = "invalid_mfa_code" });
            }

            await ResetAuthFailureCountersAsync(user);
            await _cacheClient.RemoveKeyAsync($"oidc_mfa_login:{request.MfaId}");

            await _mfaAuditService.WriteAsync(new MfaAuditEvent
            {
                EventType = LoginAuditEvents.MfaVerificationSuccess,
                UserId = user.ItemId,
                ClientId = request.ClientId,
                MfaType = user.UserMfaType,
                Status = "success"
            });

            return await AuthorizeAsync(
                mfaContext.ClientId ?? string.Empty,
                "code",
                mfaContext.RedirectUri ?? string.Empty,
                mfaContext.Scope ?? "openid profile email offline_access",
                mfaContext.State ?? string.Empty,
                mfaContext.Nonce ?? string.Empty,
                mfaContext.CodeChallenge ?? string.Empty,
                mfaContext.CodeChallengeMethod ?? "S256",
                null,
                mfaContext.TenantId ?? string.Empty,
                httpRequest,
                httpResponse,
                user.ItemId,
                false,
                mfaCompleted: true);
        }

        private async Task<IActionResult> StartOidcMfaChallengeAsync(User user, OidcLoginRequest request, string? tenantId)
        {
            var otpService = _otpServiceFactory.GetOTPService(user.UserMfaType);
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
                CodeChallengeMethod = request.CodeChallengeMethod ?? "S256",
                TenantId = tenantId ?? string.Empty
            };

            await _cacheClient.AddStringValueAsync(
                $"oidc_mfa_login:{challengeResponse.MfaId}",
                JsonSerializer.Serialize(mfaContext),
                300);

            return new OkObjectResult(new
            {
                error = OAuthError.MfaEnabled,
                error_description = "Mfa code required",
                mfa_id = challengeResponse.MfaId,
                user_mfa = user.UserMfaType.ToString()
            });
        }

        private async Task<bool> IsMfaRequiredAsync(User user)
        {
            var decision = await _mfaPolicyService.EvaluateAsync(user, clientId: null);
            return decision.Required;
        }

        private async Task<OidcCaptchaEvaluation> EvaluateOidcCaptchaAsync(User user, string? captchaCode)
        {
            if (!CaptchaGate.IsCaptchaRequired(user))
            {
                return OidcCaptchaEvaluation.Pass();
            }

            var captchaConfiguration = await _captchaConfigurationService.GetCaptchaConfigurationAsync();
            if (captchaConfiguration == null || !captchaConfiguration.IsEnable)
            {
                return OidcCaptchaEvaluation.Pass();
            }

            if (string.IsNullOrWhiteSpace(captchaCode))
            {
                return OidcCaptchaEvaluation.Require(OAuthError.CaptchaEnabled, "Captcha verification is required", captchaConfiguration.CaptchaKey, CaptchaOutcome.Missing);
            }

            var verifyCaptchaResponse = await _captchaService.VerifyCaptchaAsync(new VerifyCaptchaRequest
            {
                VerificationCode = captchaCode,
                ConfigurationName = captchaConfiguration.Provider
            });

            if (verifyCaptchaResponse.Verified)
            {
                return OidcCaptchaEvaluation.Pass();
            }

            return OidcCaptchaEvaluation.Require(OAuthError.CaptchaInvalid, "Captcha answer is invalid. Please try again.", captchaConfiguration.CaptchaKey, CaptchaOutcome.Invalid);
        }

        private static IActionResult BuildCaptchaResult(OidcCaptchaEvaluation evaluation)
        {
            return new BadRequestObjectResult(new
            {
                error = evaluation.Error,
                error_description = evaluation.ErrorDescription,
                captcha_required = true,
                captcha_site_key = evaluation.SiteKey
            });
        }

        private enum CaptchaOutcome
        {
            Pass,
            Missing,
            Invalid
        }

        private sealed class OidcCaptchaEvaluation
        {
            public bool Required { get; init; }
            public string? Error { get; init; }
            public string? ErrorDescription { get; init; }
            public string? SiteKey { get; init; }
            public CaptchaOutcome Outcome { get; init; } = CaptchaOutcome.Pass;

            public static OidcCaptchaEvaluation Pass() => new() { Required = false, Outcome = CaptchaOutcome.Pass };

            public static OidcCaptchaEvaluation Require(string error, string description, string? siteKey, CaptchaOutcome outcome) =>
                new() { Required = true, Error = error, ErrorDescription = description, SiteKey = siteKey, Outcome = outcome };
        }

        private async Task WriteOidcLoginAuditAsync(OidcLoginRequest request, User user, HttpRequest httpRequest, string eventType, string? details)
        {
            try
            {
                var isFailure = eventType.Contains("failure", StringComparison.OrdinalIgnoreCase)
                    || eventType.Contains("locked", StringComparison.OrdinalIgnoreCase);
                var isSuccess = eventType.Contains("success", StringComparison.OrdinalIgnoreCase);

                await _auditLogRepo.CreateAsync(new AuditLogModel
                {
                    EventType = eventType,
                    UserId = user.ItemId,
                    ClientId = request.ClientId,
                    TenantId = request.TenantId ?? BlocksContext.GetContext()?.TenantId,
                    IpAddress = GetClientIpAddress(httpRequest),
                    UserAgent = httpRequest.Headers.UserAgent.ToString(),
                    Severity = isFailure ? "WARN" : "INFO",
                    Status = isSuccess ? "success" : "failure",
                    Details = details ?? eventType
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write OIDC login audit event {EventType} for user {UserId}", eventType, user.ItemId);
            }
        }

        private static List<string> BuildAmr(User user, bool mfaCompleted)
        {
            var amr = new List<string> { "pwd" };
            if (mfaCompleted)
            {
                amr.Add(user.UserMfaType == UserMfaType.TOTP ? "totp" : "otp");
            }

            return amr;
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

        private sealed class OidcMfaLoginContext
        {
            public string UserId { get; set; } = string.Empty;
            public string ClientId { get; set; } = string.Empty;
            public string RedirectUri { get; set; } = string.Empty;
            public string Scope { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
            public string Nonce { get; set; } = string.Empty;
            public string CodeChallenge { get; set; } = string.Empty;
            public string CodeChallengeMethod { get; set; } = "S256";
            public string TenantId { get; set; } = string.Empty;
        }

        public bool VerifyPassword(string? password, string? passwordHash, string? optionalSalt = null)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(passwordHash))
            {
                return false;
            }

            try
            {
                return BCryptNet.Verify(BuildPasswordMaterial(password, optionalSalt), passwordHash);
            }
            catch (BCrypt.Net.SaltParseException ex)
            {
                _logger.LogWarning(ex, "Password hash is not a valid BCrypt hash format.");
                return false;
            }
        }

        private static string BuildPasswordMaterial(string password, string? optionalSalt)
        {
            return string.IsNullOrWhiteSpace(optionalSalt)
                ? password
                : $"{password}::{optionalSalt}";
        }

        public async Task<IActionResult> AuthorizeAsync(
            string client_id,
            string response_type,
            string redirect_uri,
            string scope,
            string state,
            string nonce,
            string code_challenge,
            string code_challenge_method,
            string? prompt,
            string? tenant_id,
            HttpRequest request,
            HttpResponse response,
            string? blocksUserId = null,
            bool returnRedirectResponse = true,
            bool mfaCompleted = false)
        {
            var canRedirectToClient = false;

            try
            {
                var authorizeRequest = new AuthorizeRequest
                {
                    ClientId = client_id,
                    ResponseType = response_type,
                    RedirectUri = redirect_uri,
                    Scope = scope,
                    State = state,
                    Nonce = nonce,
                    CodeChallenge = code_challenge,
                    CodeChallengeMethod = code_challenge_method,
                    Prompt = prompt
                };

                var validationResult = _authorizeValidator.Validate(authorizeRequest);

                if (!validationResult.IsValid)
                {
                    _logger.LogWarning($"Authorization request validation failed for {client_id}: {string.Join(", ", validationResult.Errors)}");

                    var errorParams = new Dictionary<string, string>
                    {
                        { "error", "invalid_request" },
                        { "error_description", string.Join("; ", validationResult.Errors) },
                        { "state", state }
                    };

                    if (returnRedirectResponse && !string.IsNullOrWhiteSpace(redirect_uri))
                    {
                        return new RedirectResult(BuildRedirectUri(redirect_uri, errorParams));
                    }

                    return new BadRequestObjectResult(new
                    {
                        error = "invalid_request",
                        error_description = string.Join("; ", validationResult.Errors)
                    });
                }

                var effectiveSessionId = request.Cookies[$"{IdpConstants.IdpSessionCookieName}_{tenant_id}"];

                string? resolvedUserId = blocksUserId;

                if (!string.IsNullOrWhiteSpace(effectiveSessionId))
                {
                    var session = await _sessionRepo.GetBySessionIdAsync(effectiveSessionId);
                    if (session != null && !session.RevokedAt.HasValue && !session.IsExpired())
                    {
                        var sessionAccounts = session.Accounts.AsEnumerable();
                        if (!string.IsNullOrWhiteSpace(tenant_id))
                        {
                            sessionAccounts = sessionAccounts.Where(a => string.Equals(a.TenantId, tenant_id, StringComparison.OrdinalIgnoreCase));
                        }

                        var filteredAccounts = sessionAccounts.ToList();

                        if (filteredAccounts.Count == 1)
                        {
                            resolvedUserId = filteredAccounts[0].UserId;
                            await _sessionRepo.UpdateActivityAsync(effectiveSessionId);
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(resolvedUserId))
                {
                    _logger.LogInformation($"Unauthenticated authorization request for {client_id}");
                    return new RedirectResult(BuildLoginUrl(client_id, response_type, redirect_uri, scope, state, nonce, code_challenge, code_challenge_method, tenant_id));
                }

                var lockoutCheckUser = await _userRepository.GetUserByIdAsync(resolvedUserId);
                if (lockoutCheckUser != null
                    && lockoutCheckUser.LockoutUntilUtc.HasValue
                    && lockoutCheckUser.LockoutUntilUtc.Value > DateTime.UtcNow)
                {
                    _logger.LogWarning("Authorize request denied for locked account {UserId}", resolvedUserId);
                    return new BadRequestObjectResult(new
                    {
                        error = "account_locked",
                        error_description = "Account is temporarily locked due to failed authentication attempts"
                    });
                }

                await EnsureIdpSessionAsync(request, response, effectiveSessionId, resolvedUserId, tenant_id);

                var client = await _authenticationRepository.GetOidcClientRegistrationAsync(client_id);
                if (client == null)
                {
                    _logger.LogWarning($"Unknown client: {client_id}");
                    return new BadRequestObjectResult(new { error = "invalid_client" });
                }

                if (!client.RedirectUris.Contains(redirect_uri))
                {
                    _logger.LogWarning($"Invalid redirect_uri for {client_id}: {redirect_uri}");
                    return new BadRequestObjectResult(new { error = "invalid_request", error_description = "Invalid redirect_uri" });
                }

                var cacheKey = $"idp_flow:{state}";
                var flowContextJson = await _cacheClient.GetStringValueAsync(cacheKey);
                var forwardedToContext = flowContextJson !=null ?  JsonSerializer.Deserialize<FlowContext>(flowContextJson) : null;

                canRedirectToClient = true;

                IActionResult BuildAuthorizeError(string error, string errorDescription)
                {
                    if (returnRedirectResponse && canRedirectToClient)
                    {
                        var errorParams = new Dictionary<string, string>
                        {
                            { "error", error },
                            { "error_description", errorDescription },
                            { "state", state },
                            { "forwardedTo", forwardedToContext?.ForwardedTo ?? string.Empty },
                        };

                        return new RedirectResult(BuildRedirectUri(redirect_uri, errorParams));
                    }

                    return new BadRequestObjectResult(new
                    {
                        error,
                        error_description = errorDescription
                    });
                }

                var user = await _userRepository.GetUserByIdAsync(resolvedUserId);
                if (user == null)
                {
                    return BuildAuthorizeError("access_denied", "User not found");
                }

                if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
                {
                    return BuildAuthorizeError("account_locked", "Account is temporarily locked due to failed authentication attempts");
                }

                var effectiveOrganizationId = ResolveEffectiveOrganizationId(user);
                await PersistLastUsedOrganizationAsync(user, effectiveOrganizationId);

                var authCode = GenerateRandomCode(32);
                var amr = BuildAmr(user, mfaCompleted);

                var codeModel = new AuthorizationCodeModel
                {
                    Code = authCode,
                    ClientId = client_id,
                    TenantId = tenant_id,
                    UserId = resolvedUserId,
                    OrganizationId = effectiveOrganizationId,
                    RedirectUri = redirect_uri,
                    Scope = scope,
                    Nonce = nonce,
                    State = state,
                    CodeChallenge = code_challenge,
                    CodeChallengeMethod = code_challenge_method,
                    Amr = amr,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                    CreatedAt = DateTime.UtcNow,
                    CreatedByIpAddress = GetClientIpAddress(request),
                };

                // Blocks Cloud Impersonation Support
                var userPrincipal = await _authenticationService.GetPrincipalFromTokenAsync(request, BlocksContext.GetContext()?.TenantId ?? "", IsUserInfoGetRequest: false);

                if (userPrincipal != null)
                {
                    bool.TryParse(userPrincipal?.FindFirst("impersonated")?.Value, out bool impersonated);

                    var claimUserId = string.IsNullOrWhiteSpace(userPrincipal?.FindFirst("sub")?.Value) ?
                                        userPrincipal?.FindFirst("user_id")?.Value :
                                        userPrincipal?.FindFirst("sub")?.Value;

                    var claimTenantId = userPrincipal?.FindFirst("tenant_id")?.Value;

                    codeModel.Impersonated = impersonated;
                    codeModel.ImpersonatedUserId = claimUserId;
                    codeModel.TargetedTenantId = claimTenantId;
                }

                _logger.LogDebug("Authorization code model: {CodeModel}", codeModel);

                await _authCodeRepo.CreateAsync(codeModel);

                _logger.LogInformation($"Authorization code issued for user {resolvedUserId}, client {client_id}");

                var callbackParams = new Dictionary<string, string>
                {
                    { "code", authCode },
                    { "state", state },
                    { "tenant_id", tenant_id ?? string.Empty },
                    { "forwardedTo", forwardedToContext?.ForwardedTo ?? string.Empty }
                };

                var callbackUri = BuildRedirectUri(redirect_uri, callbackParams);

                if (returnRedirectResponse)
                {
                    return new RedirectResult(callbackUri);
                }

                return new OkObjectResult(new
                {
                    redirect_uri = callbackUri
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in authorization endpoint");

                if (returnRedirectResponse && canRedirectToClient)
                {
                    var errorParams = new Dictionary<string, string>
                    {
                        { "error", "server_error" },
                        { "error_description", "Internal server error" },
                        { "state", state }
                    };

                    return new RedirectResult(BuildRedirectUri(redirect_uri, errorParams));
                }

                return new ObjectResult(new { error = "server_error", error_description = "Internal server error" })
                {
                    StatusCode = 500
                };
            }
        }


        public async Task<IActionResult> TokenAsync(string grantType, HttpRequest request)
        {
            try
            {
                if (grantType == "authorization_code")
                {
                    return await ExchangeAuthorizationCode(request);
                }

                if (grantType == "refresh_token")
                {
                    return await RotateRefreshToken(request);
                }

                if (grantType == "client_credentials")
                {
                    return await IssueClientCredentialsToken(request);
                }

                return new BadRequestObjectResult(new { error = "unsupported_grant_type", error_description = $"Grant type '{grantType}' not supported" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in token endpoint");
                return new ObjectResult(new { error = "server_error" }) { StatusCode = 500 };
            }
        }

        private async Task<IActionResult> IssueClientCredentialsToken(HttpRequest request)
        {
            var clientId = request.Form["client_id"].ToString();
            var clientSecret = request.Form["client_secret"].ToString();

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                TryReadBasicClientAuthentication(request, out clientId, out clientSecret);
            }

            var organizationId = request.Form["organization_id"].ToString();
            if (string.IsNullOrWhiteSpace(organizationId))
            {
                organizationId = request.Form["org_id"].ToString();
            }

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return new BadRequestObjectResult(new { error = "invalid_client", error_description = "Missing client authentication" });
            }

            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            if (authConfiguration == null)
            {
                return new BadRequestObjectResult(new { error = "server_error", error_description = "Authentication configuration missing" });
            }

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.ClientCredential,
                ClientId = clientId,
                ClientSecret = clientSecret,
                OrganizationId = organizationId,
                Request = request
            };

            var result = await _clientCredentialAuthorizationService.AuthenticateAsync(tokenRequest, authConfiguration);
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                var statusCode = string.Equals(result.Error, "invalid_client", StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.Status401Unauthorized
                    : StatusCodes.Status400BadRequest;

                return new ObjectResult(new
                {
                    error = result.Error,
                    error_description = result.ErrorDescription
                })
                { StatusCode = statusCode };
            }

            return new OkObjectResult(new TokenResponse
            {
                AccessToken = result.AccessToken,
                TokenType = "Bearer",
                ExpiresIn = result.ExpiresIn
            });
        }

        #region OIDC Exchange (Reusable API Block)

        private async Task<IActionResult> ExchangeAuthorizationCode(HttpRequest request)
        {
            var code = request.Form["code"].ToString();
            var code_verifier = request.Form["code_verifier"].ToString();
            var client_id = request.Form["client_id"].ToString();
            var redirect_uri = request.Form["redirect_uri"].ToString();

            if (string.IsNullOrWhiteSpace(client_id))
            {
                TryReadBasicClientAuthentication(request, out client_id, out _);
            }

            // Tenant ID resolution: form > query > header (X-Blocks-Key)
            var tenant_id = !string.IsNullOrWhiteSpace(request.Form["tenant_id"].ToString())
                ? request.Form["tenant_id"].ToString()
                : (!string.IsNullOrWhiteSpace(request.Query["tenant_id"].ToString())
                    ? request.Query["tenant_id"].ToString()
                    : (request.Headers.TryGetValue("X-Blocks-Key", out var headerValue)
                        ? headerValue.ToString()
                        : string.Empty));

            return await ExchangeAuthorizationCodeCore(code, code_verifier, client_id, redirect_uri, tenant_id, request, request.HttpContext.Response);
        }

        // Orchestrator: issue token set, write cookies, then return metadata-only response.
        private async Task<IActionResult> ExchangeAuthorizationCodeCore(string code, string code_verifier, string client_id, string redirect_uri, string tenant_id, HttpRequest request, HttpResponse response)
        {
            var exchangeResult = await ExchangeAuthorizationCodeToTokenSetAsync(code, code_verifier, client_id, redirect_uri, tenant_id, request);
            if (exchangeResult.ErrorResult != null)
            {
                return exchangeResult.ErrorResult;
            }

            // Get client registration to check token delivery mode
            var clientRegistration = await _authenticationRepository.GetOidcClientRegistrationAsync(client_id);
            var useTokensCookie = clientRegistration?.UseTokensCookie ?? true;

            // Validate tokens are present before proceeding
            if (string.IsNullOrWhiteSpace(exchangeResult.AccessToken))
            {
                _logger.LogError($"Access token generation failed for client {client_id}");
                return new BadRequestObjectResult(new { error = "server_error", error_description = "Failed to generate access token" });
            }

            if (useTokensCookie && exchangeResult.CanSetCookies)
            {
                var cookieDomain = exchangeResult.CookieDomain;
                var tokenDomain = exchangeResult.Domain ?? exchangeResult.EffectiveTenantId;
                var cookiesSet = AppendAccessAndRefreshTokenCookies(
                    response,
                    tokenDomain,
                    exchangeResult.AccessToken,
                    exchangeResult.RefreshToken,
                    cookieDomain,
                    exchangeResult.AccessExpiry,
                    exchangeResult.RefreshExpiry);

                if (!cookiesSet)
                {
                    _logger.LogWarning($"Failed to set authentication cookies for client {client_id}, domain {tokenDomain}. Falling back to token response body.");
                    // Fallback: return tokens in response body instead of cookies
                    return new OkObjectResult(new
                    {
                        access_token = exchangeResult.AccessToken,
                        id_token = exchangeResult.IdToken,
                        refresh_token = exchangeResult.RefreshToken,
                        token_type = "Bearer",
                        expires_in = exchangeResult.ExpiresIn,
                        scope = exchangeResult.Scope,
                        cookie_delivery_failed = true
                    });
                }

                return new OkObjectResult(new
                {
                    id_token = exchangeResult.IdToken,
                    token_type = "Bearer",
                    expires_in = exchangeResult.ExpiresIn,
                    scope = exchangeResult.Scope,
                    cookie_set = true
                });
            }

            // Fallback: client not configured for cookie-based token delivery or domain resolution failed
            if (useTokensCookie && !exchangeResult.CanSetCookies)
            {
                _logger.LogWarning("Cannot set cookies for client {ClientId}: domain resolution failed. Returning tokens in response body.", client_id);
            }

            return new OkObjectResult(new
            {
                access_token = exchangeResult.AccessToken,
                id_token = exchangeResult.IdToken,
                refresh_token = exchangeResult.RefreshToken,
                token_type = "Bearer",
                expires_in = exchangeResult.ExpiresIn,
                scope = exchangeResult.Scope,
                cookie_set = false
            });

        }

        // Grouped issuance block: validate code + PKCE + client, then build access/id/refresh token set.
        private async Task<OidcExchangeResult> ExchangeAuthorizationCodeToTokenSetAsync(string code, string code_verifier, string client_id, string redirect_uri, string tenant_id, HttpRequest request)
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(client_id))
            {
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_request", error_description = "Missing required parameters" }));
            }

            var tenantId = !string.IsNullOrWhiteSpace(tenant_id)
                ? tenant_id
                : request.HttpContext.User.FindFirst("tenant_id")?.Value;

            var authCode = await _authCodeRepo.GetByCodeAsync(code);
            if (authCode == null)
            {
                _logger.LogWarning($"Authorization code not found: {code}");
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Authorization code is invalid or expired" }));
            }

            if (!string.IsNullOrWhiteSpace(tenantId)
                && !string.IsNullOrWhiteSpace(authCode.TenantId)
                && !string.Equals(authCode.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning($"Tenant mismatch for code exchange. Presented tenant: {tenantId}, code tenant: {authCode.TenantId}");
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Tenant mismatch" }));
            }

            if (authCode.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning($"Authorization code expired: {code}");
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Authorization code has expired" }));
            }

            var client = await _authenticationRepository.GetOidcClientRegistrationAsync(client_id);
            if (client == null || client.ClientId != authCode.ClientId)
            {
                _logger.LogWarning("Client validation failed for code exchange");
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_client" }));
            }

            if (!await HasOidcClientConfigurationAsync(client_id))
            {
                _logger.LogWarning($"OIDC client config missing for code exchange: {client_id}");
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_client", error_description = "Client configuration not found" }));
            }

            if (authCode.RedirectUri != redirect_uri)
            {
                _logger.LogWarning("Redirect URI mismatch for code exchange");
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Redirect URI mismatch" }));
            }

            if (!string.IsNullOrWhiteSpace(code_verifier))
            {
                var pkceValid = await _pkceService.ValidateVerifierAsync(authCode.CodeChallenge, code_verifier, authCode.CodeChallengeMethod);
                if (!pkceValid)
                {
                    _logger.LogWarning($"PKCE validation failed for client {client_id}");
                    return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "PKCE code_verifier is invalid" }));
                }
            }

            var user = await _userRepository.GetUserByIdAsync(authCode.UserId);
            if (user == null)
            {
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "User not found" }));
            }

            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
            {
                _logger.LogWarning("Token exchange denied for locked account {UserId}", authCode.UserId);
                return OidcExchangeResult.FromError(new ObjectResult(new { error = "account_locked", error_description = "Account is temporarily locked due to failed authentication attempts" })
                {
                    StatusCode = StatusCodes.Status423Locked
                });
            }

            var effectiveTenantId = authCode.TenantId ?? tenant_id ?? "default";
            var tenant = _tenants.GetTenantByID(effectiveTenantId);
            var allowedScopes = await ResolveAllowedScopesAsync(client);
            var allowedServiceAccessResources = await ResolveAllowedServiceAccessResourcesAsync(client.ClientId);
            var resolvedClaims = await _authorizationClaimsResolver.ResolveAsync(
                user,
                authCode.OrganizationId,
                authCode.Scope,
                allowedServiceAccessResources,
                requireExplicitScope: true);

            var tenantAudience = DomainResolver.GetAudience(tenant);

            var fullName = string.Join(' ', new[] { user.FirstName, user.LastName }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            var claims = new OidcClaims
            {
                Sub = authCode.UserId,
                TenantId = effectiveTenantId,
                OrgId = authCode.OrganizationId,
                Nonce = authCode.Nonce,
                AuthTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ClientId = client_id,
                Audience = tenantAudience,
                Scope = authCode.Scope,
                Email = user.Email,
                Name = string.IsNullOrWhiteSpace(fullName) ? null : fullName,
                UserName = user.UserName,
                Amr = authCode.Amr is { Count: > 0 } ? authCode.Amr : ["pwd"],
                Roles = resolvedClaims.Roles,
                Resources = resolvedClaims.Resources,
                Permissions = resolvedClaims.Permissions
            };

            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            var accessTokenLifetimeSeconds = Math.Max((authConfiguration?.AccessTokenValidForNumberMinutes ?? IdentityConfiguration.DefaultAccessTokenValidForNumberMinutes) * 60, 60);
            var absoluteRefreshTokenLifetimeMinutes = Math.Max(authConfiguration?.AbsoluteRefreshTokenValidForNumberMinutes ?? IdentityConfiguration.DefaultRememberMeRefreshTokenValidForNumberMinutes, 1);

            var issuer = DomainResolver.GetIssuer(tenant);
            var idToken = await _tokenService.GenerateIdTokenAsync(claims, issuer, accessTokenLifetimeSeconds);
            var accessToken = await _tokenService.GenerateAccessTokenAsync(claims, issuer, accessTokenLifetimeSeconds);
            var refreshTokenModel = await _tokenService.GenerateRefreshTokenAsync(claims, issuer, false);

            refreshTokenModel.UserId = authCode.UserId;
            refreshTokenModel.ClientId = client_id;
            refreshTokenModel.TenantId = effectiveTenantId;
            refreshTokenModel.OrgId = authCode.OrganizationId;
            refreshTokenModel.Audience = tenantAudience;
            refreshTokenModel.Scope = authCode.Scope;
            refreshTokenModel.IpAddress = GetClientIpAddress(request);
            refreshTokenModel.UserAgent = request.Headers["User-Agent"].ToString();
            await _refreshTokenRepo.CreateAsync(refreshTokenModel);

            _logger.LogInformation($"Tokens issued for user {authCode.UserId}, client {client_id}");

            var (domain, cookieDomain, isResolved) = DomainResolver.ResolveDomain(tenant, request);
            var accessExpiry = DateTime.UtcNow.AddSeconds(accessTokenLifetimeSeconds);
            var refreshExpiry = refreshTokenModel.AbsoluteExpiry == default
                ? DateTime.UtcNow.AddMinutes(absoluteRefreshTokenLifetimeMinutes)
                : refreshTokenModel.AbsoluteExpiry;

            return OidcExchangeResult.FromTokens(
                accessToken,
                idToken,
                refreshTokenModel.TokenId,
                effectiveTenantId,
                isResolved ? domain : null,
                cookieDomain,
                authCode.Scope,
                accessTokenLifetimeSeconds,
                accessExpiry,
                refreshExpiry);
        }

        private static bool AppendAccessAndRefreshTokenCookies(
            HttpResponse response,
            string tokenDomain,
            string? accessToken,
            string? refreshToken,
            string? cookieDomain,
            DateTime accessExpiry,
            DateTime refreshExpiry)
        {
            // Validate tokens are not empty before attempting to set cookies
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return false; // Cannot set cookies without valid access token
            }

            var isLocal = DomainResolver.IsLocalhost();
            var accessOptions = isLocal
                ? DomainResolver.CreateLoopbackCookieOptions(cookieDomain, accessExpiry)
                : DomainResolver.CreateProductionCookieOptions(cookieDomain, accessExpiry);
            var refreshOptions = isLocal
                ? DomainResolver.CreateLoopbackCookieOptions(cookieDomain, refreshExpiry)
                : DomainResolver.CreateProductionCookieOptions(cookieDomain, refreshExpiry);

            response.Cookies.Append($"{tokenDomain}", accessToken, accessOptions);

            // Only append refresh token if provided
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                response.Cookies.Append($"{IdpConstants.RefreshTokenCookieName}_{tokenDomain}", refreshToken, refreshOptions);
            }

            return true;
        }

        // Internal transport model for exchange outcome: either error result or issued token set.
        private sealed class OidcExchangeResult
        {
            public IActionResult? ErrorResult { get; private set; }
            public string AccessToken { get; private set; } = string.Empty;
            public string IdToken { get; private set; } = string.Empty;
            public string RefreshToken { get; private set; } = string.Empty;
            public string EffectiveTenantId { get; private set; } = string.Empty;
            public string? Domain { get; private set; }
            public string? CookieDomain { get; private set; }
            public bool CanSetCookies => !string.IsNullOrWhiteSpace(Domain);
            public string Scope { get; private set; } = string.Empty;
            public int ExpiresIn { get; private set; }
            public DateTime AccessExpiry { get; private set; }
            public DateTime RefreshExpiry { get; private set; }

            public static OidcExchangeResult FromError(IActionResult errorResult)
            {
                return new OidcExchangeResult { ErrorResult = errorResult };
            }

            public static OidcExchangeResult FromTokens(
                string accessToken,
                string idToken,
                string refreshToken,
                string effectiveTenantId,
                string? domain,
                string? cookieDomain,
                string scope,
                int expiresIn,
                DateTime accessExpiry,
                DateTime refreshExpiry)
            {
                return new OidcExchangeResult
                {
                    AccessToken = accessToken,
                    IdToken = idToken,
                    RefreshToken = refreshToken,
                    EffectiveTenantId = effectiveTenantId,
                    Domain = domain,
                    CookieDomain = cookieDomain,
                    Scope = scope,
                    ExpiresIn = expiresIn,
                    AccessExpiry = accessExpiry,
                    RefreshExpiry = refreshExpiry
                };
            }
        }

        #endregion

        private async Task<IActionResult> RotateRefreshToken(HttpRequest request)
        {
            var client_id = request.Form["client_id"].ToString();
            if (string.IsNullOrEmpty(client_id))
            {
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "Missing client_id" });
            }

            var client = await _authenticationRepository.GetOidcClientRegistrationAsync(client_id);
            if (client is null)
            {
                return new BadRequestObjectResult(new { error = "invalid_client", error_description = "client not found" });
            }

            var bc = BlocksContext.GetContext();
            var tenant = _tenants.GetTenantByID(bc?.TenantId ?? "default");
            string refresh_token = "";

            if (client.UseTokensCookie)
            {
                var (domain, _, isResolved) = DomainResolver.ResolveDomain(tenant, request);
                var cookieKey = isResolved && !string.IsNullOrWhiteSpace(domain)
                    ? $"{IdpConstants.RefreshTokenCookieName}_{domain}"
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(cookieKey))
                {
                    refresh_token = request.HttpContext.Request.Cookies[cookieKey] ?? string.Empty;
                }

                // For API/postman callers (or unresolved domain), accept body token as runtime fallback.
                if (string.IsNullOrWhiteSpace(refresh_token))
                {
                    refresh_token = request.Form["refresh_token"].ToString();
                }
            }
            else
            {
                refresh_token = request.Form["refresh_token"].ToString();
            }

            if (string.IsNullOrWhiteSpace(refresh_token))
            {
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "refresh token not found" });
            }

            // Delegate to unified refresh token authentication service (same as ExecuteRefreshAsync)
            var refreshRequest = new RefreshRequest
            {
                RefreshToken = refresh_token,
                ClientId = client_id
            };

            var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            if (configuration == null)
            {
                return new BadRequestObjectResult(new { error = "auth_config_missing" });
            }

            var cachedRefreshToken = await _cacheClient.GetStringValueAsync(refresh_token);
            if (string.IsNullOrWhiteSpace(cachedRefreshToken))
            {
                return new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Refresh token is invalid or expired" });
            }

            var tokenCache = JsonSerializer.Deserialize<RefreshTokenCache>(cachedRefreshToken);
            if (tokenCache == null || string.IsNullOrWhiteSpace(tokenCache.UserId))
            {
                return new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Refresh token is invalid or expired" });
            }

            if (string.IsNullOrWhiteSpace(tokenCache.ClientId) || !await HasOidcClientConfigurationAsync(tokenCache.ClientId))
            {
                return new UnauthorizedObjectResult(new { error = "invalid_client", error_description = "Client configuration not found" });
            }

            // Defense-in-depth: Validate sent client_id matches the cached/bound client_id
            if (!string.IsNullOrWhiteSpace(refreshRequest.ClientId) &&
                !string.Equals(refreshRequest.ClientId, tokenCache.ClientId, StringComparison.OrdinalIgnoreCase))
            {
                return new UnauthorizedObjectResult(new { error = "invalid_client", error_description = "Client mismatch: sent client_id does not match token binding" });
            }

            var currentTenantId = BlocksContext.GetContext()?.TenantId;
            if (!string.Equals(tokenCache.TenantId, currentTenantId, StringComparison.OrdinalIgnoreCase))
            {
                return new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Refresh token tenant mismatch" });
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
                ClientId = tokenCache.ClientId,
                RefreshToken = refresh_token,
                Request = request
            };

            var response = await _refreshTokenAuthenticationService.AuthenticateAsync(tokenRequest, configuration, user);

            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                var statusCode = response.StatusCode > 0 ? response.StatusCode : StatusCodes.Status400BadRequest;
                return new ObjectResult(new
                {
                    error = response.Error,
                    error_description = response.ErrorDescription
                })
                {
                    StatusCode = statusCode
                };
            }

            if (tokenCache.Impersonated && !string.IsNullOrWhiteSpace(tokenCache.ImpersonationId))
            {
                var existingSession = await _authenticationRepository.GetImpersonationSessionByIdAsync(tokenCache.ImpersonationId);
                return await _authenticationService.ExecuteImpersonateAsync(
                    new ImpersonateRequest
                    {
                        TargetTenantId = existingSession.TargetTenantId,
                        OrganizationId = tokenCache.OrganizationId,
                        ImpersonationId = tokenCache.ImpersonationId,
                        ImpersontingUserId = tokenCache.UserId,
                        RefreshToken = response.RefreshToken
                    },
                    request.HttpContext.Request,
                    request.HttpContext.Response
                );
            }

            var useTokensCookie = client.UseTokensCookie;
            if (useTokensCookie)
            {
                var tenantId = BlocksContext.GetContext()?.TenantId ?? "default";
                var resolvedTenant = _tenants.GetTenantByID(tenantId);
                var (domain, _, _) = DomainResolver.ResolveDomain(resolvedTenant, request);
                var cookiesSet = AppendCookies(response, request.HttpContext.Response, domain);
                if (cookiesSet)
                {
                    return new OkObjectResult(new
                    {
                        token_type = response.TokenType,
                        expires_in = response.ExpiresIn,
                        scope = response.Scope,
                        cookie_set = true
                    });
                }
            }

            return new OkObjectResult(new
            {
                access_token = response.AccessToken,
                refresh_token = response.RefreshToken,
                token_type = response.TokenType,
                expires_in = response.ExpiresIn,
                scope = response.Scope,
                id_token = response.IdToken,
                cookie_set = false
            });
        }

        private static string GenerateRandomCode(int length)
        {
            byte[] buffer = new byte[length];
            RandomNumberGenerator.Fill(buffer);
            return Convert.ToBase64String(buffer).Replace("/", "_").Replace("+", "-").Substring(0, 43);
        }

        private async Task<IReadOnlyCollection<string>> ResolveAllowedScopesAsync(OidcClientRegistration client)
        {
            if (client.AllowedScopes is { Count: > 0 })
            {
                return client.AllowedScopes
                    .Where(scope => !string.IsNullOrWhiteSpace(scope))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return [];
        }

        private async Task<IReadOnlyCollection<string>> ResolveAllowedServiceAccessResourcesAsync(string? clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return [];
            }

            var tenantOidcClient = await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
            if (tenantOidcClient == null || tenantOidcClient.AllowedServiceAccessResources.Count == 0)
            {
                return [];
            }

            return tenantOidcClient.AllowedServiceAccessResources
                .Where(resource => !string.IsNullOrWhiteSpace(resource))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildRedirectUri(string baseUri, Dictionary<string, string> parameters)
        {
            var sb = new StringBuilder(baseUri);
            sb.Append(baseUri.Contains("?") ? "&" : "?");

            foreach (var param in parameters.Where(p => !string.IsNullOrEmpty(p.Value)))
            {
                sb.Append(Uri.EscapeDataString(param.Key));
                sb.Append("=");
                sb.Append(Uri.EscapeDataString(param.Value));
                sb.Append("&");
            }

            return sb.ToString().TrimEnd('&');
        }

        private static string BuildLoginUrl(
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

        private static void TryReadBasicClientAuthentication(HttpRequest request, out string clientId, out string clientSecret)
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


        private async Task EnsureIdpSessionAsync(HttpRequest request, HttpResponse response, string? currentSessionId, string userId, string tenantId)
        {
            var session = string.IsNullOrWhiteSpace(currentSessionId)
                ? null
                : await _sessionRepo.GetBySessionIdAsync(currentSessionId);

            if (session == null || session.RevokedAt.HasValue || session.IsExpired())
            {
                var newSession = new IdpSessionModel
                {
                    SessionId = Guid.NewGuid().ToString("n"),
                    TenantId = tenantId,
                    Accounts =
                    [
                        new IdpSessionAccount
                        {
                            UserId = userId,
                            TenantId = tenantId,
                            DisplayName = userId,
                            LoginAt = DateTime.UtcNow
                        }
                    ],
                    IpAddress = GetClientIpAddress(request),
                    CreatedAt = DateTime.UtcNow,
                    LastActivityAt = DateTime.UtcNow,
                    IdleExpiry = DateTime.UtcNow.Add(GetIdpSessionIdleTimeout()),
                    AbsoluteExpiry = DateTime.UtcNow.Add(GetIdpSessionAbsoluteTimeout())
                };

                await _sessionRepo.CreateAsync(newSession);
                SetIdpSessionCookie(request, response, tenantId, newSession.SessionId, newSession.AbsoluteExpiry);
                return;
            }

            var accountExists = session.Accounts.Any(a =>
                string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));

            if (!accountExists)
            {
                await _sessionRepo.AddAccountAsync(session.SessionId, new IdpSessionAccount
                {
                    UserId = userId,
                    TenantId = tenantId,
                    DisplayName = userId,
                    LoginAt = DateTime.UtcNow
                });
            }
            else
            {
                await _sessionRepo.UpdateActivityAsync(session.SessionId);
            }

            SetIdpSessionCookie(request, response, tenantId, session.SessionId, session.AbsoluteExpiry);
        }

        private void SetIdpSessionCookie(
            HttpRequest httpRequest,
            HttpResponse response,
            string tenantId,
            string sessionId,
            DateTime absoluteExpiry)
        {
            var isLocal = DomainResolver.IsLocalhost();
            var domain = BlocksContext.ResolveApplicationDomain(httpRequest);
            var effectiveExpiry = absoluteExpiry == default
                ? DateTime.UtcNow.Add(GetIdpSessionAbsoluteTimeout())
                : absoluteExpiry;

            string? resolvedDomain = null;
            if (!string.IsNullOrWhiteSpace(domain))
            {
                var tenant = _tenants.GetTenantByID(tenantId);
                resolvedDomain = tenant.IsRootTenant
                    ? DomainResolver.GetRootDomain(domain)
                    : domain;
            }

            var cookieOptions = isLocal
                ? DomainResolver.CreateLoopbackCookieOptions(resolvedDomain, effectiveExpiry)
                : DomainResolver.CreateProductionCookieOptions(resolvedDomain, effectiveExpiry);

            response.Cookies.Append(
                $"{IdpConstants.IdpSessionCookieName}_{tenantId}",
                sessionId,
                cookieOptions);
        }

        private static TimeSpan GetIdpSessionIdleTimeout()
        {
            var configured = Environment.GetEnvironmentVariable("IDP_SESSION_IDLE_HOURS");
            if (double.TryParse(configured, out var hours) && hours > 0 && hours <= 168)
            {
                return TimeSpan.FromHours(hours);
            }

            return TimeSpan.FromHours(24);
        }

        private static TimeSpan GetIdpSessionAbsoluteTimeout()
        {
            var configured = Environment.GetEnvironmentVariable("IDP_SESSION_ABSOLUTE_HOURS");
            if (double.TryParse(configured, out var hours) && hours > 0 && hours <= 168)
            {
                return TimeSpan.FromHours(hours);
            }

            return TimeSpan.FromHours(5); // Default to 5 hours
        }

        private static string? ResolveEffectiveOrganizationId(User user)
        {
            if (!string.IsNullOrWhiteSpace(user.LastUsedOrganizationId)
                && user.OrganizationIds.Contains(user.LastUsedOrganizationId))
            {
                return user.LastUsedOrganizationId;
            }

            if (user.OrganizationIds.Contains("default"))
            {
                return "default";
            }

            return user.OrganizationIds.FirstOrDefault()
                ?? user.Roles.Keys.FirstOrDefault()
                ?? user.Permissions.Keys.FirstOrDefault();
        }

        private async Task PersistLastUsedOrganizationAsync(User user, string? organizationId)
        {
            if (string.IsNullOrWhiteSpace(organizationId)
                || string.Equals(user.LastUsedOrganizationId, organizationId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                user.LastUsedOrganizationId = organizationId;
                await _userRepository.UpdateUserAsync(user);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist last used organization for user {UserId}", user.ItemId);
            }
        }

        private static string GetClientIpAddress(HttpRequest request)
        {
            if (request.HttpContext.Connection.RemoteIpAddress != null)
            {
                return request.HttpContext.Connection.RemoteIpAddress.ToString();
            }
            return "unknown";
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

        private async Task RevokeUserTokens(string userId, string clientId, string tenantId)
        {
            try
            {
                var userTokens = await _refreshTokenRepo.GetByUserAsync(userId, tenantId);
                var clientTokens = userTokens.Where(t => t.ClientId == clientId && !t.IsRevoked).ToList();

                foreach (var token in clientTokens)
                {
                    await _refreshTokenRepo.RevokeByTokenIdAsync(token.TokenId, "authorization_code_reuse_detected");
                }

                var auditLog = new AuditLogModel
                {
                    EventType = "code_reuse_attack",
                    UserId = userId,
                    ClientId = clientId,
                    TenantId = tenantId,
                    IpAddress = "unknown",
                    UserAgent = "unknown",
                    Severity = "CRITICAL",
                    Status = "success",
                    Timestamp = DateTime.UtcNow
                };
                await _auditLogRepo.CreateAsync(auditLog);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error revoking user tokens for {userId}");
            }
        }

        private static bool AppendCookies(TokenResponse response, HttpResponse httpResponse, string domain)
        {
            if (!string.IsNullOrWhiteSpace(response.Error))
                return false;
            if (string.IsNullOrWhiteSpace(response.AccessToken))
                return false;
            if (string.IsNullOrWhiteSpace(domain))
                return false;
            var accessCookieOptions = DomainResolver.CreateCookieOptions(response.CookieDomain, response.ExpiresUtc);
            var refreshCookieOptions = DomainResolver.CreateCookieOptions(response.CookieDomain, response.RefreshExpiresUtc);
            DeleteCookie(httpResponse, domain, accessCookieOptions, refreshCookieOptions);
            httpResponse.Cookies.Append(domain, response.AccessToken, accessCookieOptions);
            if (!string.IsNullOrWhiteSpace(response.RefreshToken))
                httpResponse.Cookies.Append($"{IdpConstants.RefreshTokenCookieName}_{domain}", response.RefreshToken, refreshCookieOptions);
            return true;
        }

        private static void DeleteCookie(HttpResponse httpResponse, string domain, CookieOptions accessCookieOptions, CookieOptions refreshCookieOptions)
        {
            httpResponse.Cookies.Delete(domain, accessCookieOptions);
            httpResponse.Cookies.Delete($"{IdpConstants.RefreshTokenCookieName}_{domain}", refreshCookieOptions);
        }

        private sealed class FlowContext
        {
            [JsonPropertyName("forwardedTo")]
            public string? ForwardedTo { get; set; } = null!;
        }
    }
}


