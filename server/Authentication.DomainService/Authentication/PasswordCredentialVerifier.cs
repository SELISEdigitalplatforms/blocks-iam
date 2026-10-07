using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using BCryptNet = BCrypt.Net.BCrypt;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Extracted from <see cref="PasswordAuthenticationService"/> without behaviour change; see
    /// <see cref="IPasswordCredentialVerifier"/>.
    /// </summary>
    public sealed class PasswordCredentialVerifier : IPasswordCredentialVerifier
    {
        public const string LockedDescription = "Account is temporarily locked due to failed login attempts";
        public const string InvalidPasswordDescription = "Invalid username or password";

        private readonly ILogger<PasswordCredentialVerifier> _logger;
        private readonly ITenants _tenants;
        private readonly IAuthenticationRepository _repository;
        private readonly IAccountService _accountService;
        private readonly IUserActivityDispatcher _userActivityDispatcher;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly ICaptchaEvaluator? _captchaEvaluator;

        public PasswordCredentialVerifier(
            ILogger<PasswordCredentialVerifier> logger,
            ITenants tenants,
            IAuthenticationRepository repository,
            IAccountService accountService,
            IUserActivityDispatcher userActivityDispatcher,
            IAuthenticationDomainService authenticationDomainService,
            ICaptchaEvaluator? captchaEvaluator = null)
        {
            _logger = logger;
            _tenants = tenants;
            _repository = repository;
            _accountService = accountService;
            _userActivityDispatcher = userActivityDispatcher;
            _authenticationDomainService = authenticationDomainService;
            _captchaEvaluator = captchaEvaluator;
        }

        public async Task<PasswordVerificationResult> VerifyAsync(
            User user,
            string? password,
            string? captchaCode,
            HttpRequest? request,
            string? tenantId)
        {
            var configuration = await _repository.GetAuthenticationConfigurationAsync();
            if (configuration == null)
            {
                return new PasswordVerificationResult
                {
                    Error = OAuthError.AuthConfigMissing,
                    StatusCode = StatusCodes.Status400BadRequest
                };
            }

            if (IsLocked(user))
            {
                await SendTimelineEventAsync(request, user.ItemId, "failed_login_account_locked", "password_auth_account_locked");
                return Locked();
            }

            var captcha = await EvaluateCaptchaAsync(user, captchaCode);
            if (captcha != null)
            {
                return captcha;
            }

            var verification = await VerifyPasswordAsync(user, password, configuration, request, tenantId);
            if (verification.Succeeded)
            {
                await ResetFailureCountersAsync(user);
            }

            return verification;
        }

        public async Task<PasswordVerificationResult> VerifyPasswordAsync(
            User user,
            string? password,
            IdentityConfiguration configuration,
            HttpRequest? request,
            string? tenantId)
        {
            if (IsLocked(user))
            {
                await SendTimelineEventAsync(request, user.ItemId, "failed_login_account_locked", "password_auth_account_locked");
                return Locked();
            }

            var tenant = !string.IsNullOrWhiteSpace(tenantId) ? _tenants.GetTenantByID(tenantId) : null;
            if (VerifyPassword(password, user.Password ?? string.Empty, tenant?.TenantSalt))
            {
                return PasswordVerificationResult.Success();
            }

            var updatedUser = await _repository.IncrementFailedLoginAndApplyLockoutAsync(
                user.ItemId,
                configuration.GetNumberOfWrongAttemptsToLockTheAccount,
                configuration.AccountLockDurationInMinutes,
                DateTime.UtcNow);

            var lockoutUntilUtc = updatedUser?.LockoutUntilUtc;

            var eventName = lockoutUntilUtc.HasValue
                ? "failed_login_and_account_locked"
                : "failed_login_invalid_password";
            var actionBy = lockoutUntilUtc.HasValue
                ? "password_auth_lock_after_failed_attempts"
                : "password_auth_failed_attempt";

            await SendTimelineEventAsync(request, user.ItemId, eventName, actionBy);

            if (lockoutUntilUtc.HasValue && updatedUser != null)
            {
                try
                {
                    await _accountService.SendAccountLockedNotificationAsync(updatedUser, lockoutUntilUtc.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send account locked notification after failed login for user: {UserId}", user.ItemId);
                }
            }

            return new PasswordVerificationResult
            {
                Error = OAuthError.InValidUseNamePassword,
                ErrorDescription = InvalidPasswordDescription,
                StatusCode = StatusCodes.Status401Unauthorized,
                AccountLockedNow = lockoutUntilUtc.HasValue
            };
        }

        public async Task ResetFailureCountersAsync(User user)
        {
            if (user.FailedLoginCount <= 0
                && !user.LastFailedLoginUtc.HasValue
                && user.FailedMfaCount <= 0
                && !user.LastFailedMfaUtc.HasValue
                && !user.LockoutUntilUtc.HasValue)
            {
                return;
            }

            await _repository.UpdatePartialAsync<User>(
                user.ItemId,
                new Dictionary<string, object>
                {
                    { nameof(User.FailedLoginCount), 0 },
                    { nameof(User.LastFailedLoginUtc), null! },
                    { nameof(User.FailedMfaCount), 0 },
                    { nameof(User.LastFailedMfaUtc), null! },
                    { nameof(User.LockoutUntilUtc), null! },
                    { nameof(User.LockoutCount), 0 }, // Reset exponential backoff counter on successful login
                    { nameof(User.LastUpdatedDate), DateTime.UtcNow },
                    { nameof(User.LastUpdatedBy), user.ItemId }
                });
        }

        public bool VerifyPassword(string? password, string? passwordHash, string? optionalSalt = null)
        {
            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordHash))
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

        public static string BuildPasswordMaterial(string password, string? optionalSalt) =>
            string.IsNullOrWhiteSpace(optionalSalt) ? password : $"{password}::{optionalSalt}";

        private static bool IsLocked(User user) =>
            user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow;

        private static PasswordVerificationResult Locked() => new()
        {
            Error = OAuthError.AccountLocked,
            ErrorDescription = LockedDescription,
            StatusCode = StatusCodes.Status423Locked
        };

        /// <summary>
        /// The CAPTCHA gate embedded login applies, keyed on the same
        /// <see cref="CaptchaGate"/> threshold and the tenant's captcha configuration.
        /// </summary>
        private async Task<PasswordVerificationResult?> EvaluateCaptchaAsync(User user, string? captchaCode)
        {
            if (_captchaEvaluator == null || !CaptchaGate.IsCaptchaRequired(user))
            {
                return null;
            }

            var configuration = await _captchaEvaluator.GetConfigurationAsync();
            if (configuration == null || !configuration.IsEnable)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(captchaCode))
            {
                return CaptchaFailure(OAuthError.CaptchaEnabled, "Captcha verification is required", configuration.CaptchaKey);
            }

            var response = await _captchaEvaluator.VerifyAsync(captchaCode, configuration.Provider);
            var verified = response.GetType().GetProperty("Verified")?.GetValue(response) is true;
            return verified
                ? null
                : CaptchaFailure(OAuthError.CaptchaInvalid, "Captcha answer is invalid. Please try again.", configuration.CaptchaKey);
        }

        private static PasswordVerificationResult CaptchaFailure(string error, string description, string? siteKey) => new()
        {
            Error = error,
            ErrorDescription = description,
            StatusCode = StatusCodes.Status400BadRequest,
            CaptchaRequired = true,
            CaptchaSiteKey = siteKey
        };

        private async Task SendTimelineEventAsync(HttpRequest? request, string userId, string eventName, string actionBy)
        {
            if (string.IsNullOrWhiteSpace(userId) || request?.HttpContext == null)
            {
                return;
            }

            await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
            {
                UserId = userId,
                Category = UserActivityCategory.Auth,
                Event = eventName,
                Source = "auth-password",
                Outcome = eventName.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) ? "success" : "failure",
                Context = new ActivityContext
                {
                    IpAddress = string.Join(",", _authenticationDomainService.GetVisitorsIpAddresses(request.HttpContext)),
                    DeviceInformation = _authenticationDomainService.GetDeviceInfo(request.Headers.UserAgent.ToString())
                },
                Metadata = new Dictionary<string, string> { { "actionBy", actionBy } }
            });
        }
    }
}
