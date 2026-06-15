using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Iam.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.OAuth.Services
{
    public class MfaAuthorizationService : ITokenService
    {
        private readonly ILogger<MfaAuthorizationService> _logger;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        private readonly IOtpServiceFactory _tpServiceFactory;
        private readonly IAuthenticationRepository _oAuthRepository;

        public MfaAuthorizationService(ILogger<MfaAuthorizationService> logger,
                                       IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
                                       IOtpServiceFactory tpServiceFactory,
                                       IAuthenticationRepository oAuthRepository)
        {
            _logger = logger;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _tpServiceFactory = tpServiceFactory;
            _oAuthRepository = oAuthRepository;
        }

        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, IdentityConfiguration authenticationConfiguration, User? user = null)
        {
            var otpService = _tpServiceFactory.GetOTPService(request.MfaType);
            var response = await otpService.VerifyAsync(new VerifyOtpRequest { AuthType = request.MfaType, MfaId = request.MfaId, VerificationCode = request.Code });

            if (response.IsValid)
            {
                user = await _oAuthRepository.GetUserByIdAsync(response.UserId);
                if (user == null)
                {
                    return new TokenResponse { Error = "invalid_request", ErrorDescription = "User not found for mfa session", StatusCode = 400 };
                }

                if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
                {
                    return new TokenResponse
                    {
                        Error = OAuthError.AccountLocked,
                        ErrorDescription = "Account is temporarily locked due to failed authentication attempts",
                        StatusCode = 423
                    };
                }

                if (!user.IsMfaVerified)
                {
                    return new TokenResponse { Error = "unverified_user_mfa", ErrorDescription = "Unverified user mfa please verify the mfa first", StatusCode = 400 };
                }

                var tokenResponse = await _oAuthJwtAccessTokenManager.ManageTokenAsync(request, authenticationConfiguration, user);
                if (string.IsNullOrWhiteSpace(tokenResponse.Error)
                    && (user.FailedLoginCount > 0
                        || user.LastFailedLoginUtc.HasValue
                        || user.FailedMfaCount > 0
                        || user.LastFailedMfaUtc.HasValue
                        || user.LockoutUntilUtc.HasValue))
                {
                    await _oAuthRepository.UpdatePartialAsync<User>(
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

                return tokenResponse;
            }

            await TrackFailedMfaAttemptAsync(response.UserId, authenticationConfiguration);

            return new TokenResponse { Error = "invalid_mfa_code", ErrorDescription = "Mfa code is not valid", StatusCode = 401 };
        }

        private async Task TrackFailedMfaAttemptAsync(string? userId, IdentityConfiguration authenticationConfiguration)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            try
            {
                await _oAuthRepository.IncrementFailedMfaAndApplyLockoutAsync(
                    userId,
                    authenticationConfiguration.GetNumberOfWrongAttemptsToLockTheAccount,
                    authenticationConfiguration.AccountLockDurationInMinutes,
                    DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to track MFA failure for user {UserId}", userId);
            }
        }
    }
}
