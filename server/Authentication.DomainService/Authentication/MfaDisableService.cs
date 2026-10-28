using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Microsoft.AspNetCore.Http;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// A user turns off their own MFA. A session alone is not enough: the caller proves the
    /// password and a fresh code of the current method, and the tenant policy must allow it.
    /// Both wrong answers count on the same lockout counters login uses.
    /// </summary>
    public sealed class MfaDisableService : IMfaDisableService
    {
        public const string MfaNotEnabled = "mfa_not_enabled";
        public const string DisableNotAllowed = "mfa_disable_not_allowed";
        public const string InvalidCode = "invalid_mfa_code";
        public const string InvalidRequest = "invalid_request";

        private readonly IAuthenticationRepository _repository;
        private readonly IPasswordCredentialVerifier _passwordVerifier;
        private readonly IMfaManagementService _mfaManagementService;
        private readonly IMfaPolicyService _mfaPolicyService;
        private readonly IUserSessionTerminator _sessionTerminator;
        private readonly IMfaAttemptGate _mfaAttemptGate;

        public MfaDisableService(
            IAuthenticationRepository repository,
            IPasswordCredentialVerifier passwordVerifier,
            IMfaManagementService mfaManagementService,
            IMfaPolicyService mfaPolicyService,
            IUserSessionTerminator sessionTerminator,
            IMfaAttemptGate mfaAttemptGate)
        {
            _mfaAttemptGate = mfaAttemptGate;
            _repository = repository;
            _passwordVerifier = passwordVerifier;
            _mfaManagementService = mfaManagementService;
            _mfaPolicyService = mfaPolicyService;
            _sessionTerminator = sessionTerminator;
        }

        public async Task<MfaDisableResult> DisableAsync(string userId, MfaDisableRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Password)
                || string.IsNullOrWhiteSpace(request.MfaId)
                || string.IsNullOrWhiteSpace(request.VerificationCode))
            {
                return MfaDisableResult.Fail(StatusCodes.Status400BadRequest, InvalidRequest,
                    "password, mfaId and verificationCode are required");
            }

            var user = await _repository.GetUserByIdAsync(userId);
            if (user == null)
            {
                return MfaDisableResult.Fail(StatusCodes.Status404NotFound, "user_not_found", "User not found");
            }

            var allowed = await CheckAllowedAsync(user);
            if (allowed != null)
            {
                return allowed;
            }

            var proof = await VerifyPasswordAsync(user, request.Password!) ?? await VerifyCodeAsync(user, request);
            if (proof != null)
            {
                return proof;
            }

            var disabled = await _mfaManagementService.DisableUserMfa(new DisableUserMfaRequest { UserId = userId });
            if (disabled.Errors != null && disabled.Errors.Count > 0)
            {
                return MfaDisableResult.Fail(StatusCodes.Status400BadRequest, disabled.Errors.Keys.First(), disabled.Errors.Values.First());
            }

            // Whoever else holds a session of this user loses it with the second factor.
            await _sessionTerminator.EndAllSessionsAsync(userId, "mfa_disabled");
            return MfaDisableResult.Success(user.UserMfaType);
        }

        private async Task<MfaDisableResult?> CheckAllowedAsync(User user)
        {
            if (IsLocked(user))
            {
                return Locked();
            }

            if (!user.MfaEnabled || user.UserMfaType == UserMfaType.None)
            {
                return MfaDisableResult.Fail(StatusCodes.Status400BadRequest, MfaNotEnabled, "MFA is not enabled for this user");
            }

            var policy = await _mfaPolicyService.EvaluateAsync(user, null);
            return policy.CanUserDisable
                ? null
                : MfaDisableResult.Fail(StatusCodes.Status403Forbidden, DisableNotAllowed, "The MFA policy does not allow turning MFA off");
        }

        private async Task<MfaDisableResult?> VerifyPasswordAsync(User user, string password)
        {
            var configuration = await _repository.GetAuthenticationConfigurationAsync() ?? new IdentityConfiguration();
            var result = await _passwordVerifier.VerifyPasswordAsync(user, password, configuration, null, BlocksContext.GetContext()?.TenantId);
            return result.Succeeded
                ? null
                : MfaDisableResult.Fail(result.StatusCode, result.Error ?? OAuthError.InValidUseNamePassword, result.ErrorDescription);
        }

        private async Task<MfaDisableResult?> VerifyCodeAsync(User user, MfaDisableRequest request)
        {
            // Only a challenge issued to this user, checked with the user's current method: the
            // caller cannot pick a weaker method or reuse someone else's challenge. Counted before
            // it is checked, so parallel codes get no more checks than the lock threshold.
            var configuration = await _repository.GetAuthenticationConfigurationAsync() ?? new IdentityConfiguration();
            var attempt = await _mfaAttemptGate.CheckAsync(user.ItemId, configuration, async () =>
            {
                var owner = await _mfaManagementService.GetChallengeOwnerAsync(request.MfaId!);
                if (!string.Equals(owner, user.ItemId, StringComparison.Ordinal))
                {
                    return false;
                }

                var verification = await _mfaManagementService.VerifyOTPAsync(new VerifyOtpRequest
                {
                    MfaId = request.MfaId,
                    VerificationCode = request.VerificationCode,
                    AuthType = user.UserMfaType,
                    IsFromTokenCall = true
                });
                return verification.IsValid;
            });

            if (attempt.IsValid)
            {
                return null;
            }

            return attempt.IsLocked
                ? Locked()
                : MfaDisableResult.Fail(StatusCodes.Status400BadRequest, InvalidCode, "The verification code is invalid or expired");
        }

        private static bool IsLocked(User user) =>
            user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow;

        private static MfaDisableResult Locked() =>
            MfaDisableResult.Fail(StatusCodes.Status423Locked, OAuthError.AccountLocked,
                "Account is temporarily locked due to failed authentication attempts");
    }
}
