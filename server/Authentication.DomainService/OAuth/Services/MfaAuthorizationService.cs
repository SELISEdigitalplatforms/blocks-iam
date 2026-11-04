using Authentication.DomainService.Authentication;
using Iam.DomainService.Utilities;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Iam.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Microsoft.Extensions.Logging;
using Blocks.Genesis.Entities.Iam;

namespace Authentication.DomainService.OAuth.Services
{
    public sealed class MfaAuthorizationService : ITokenService
    {
        private readonly ILogger<MfaAuthorizationService> _logger;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        private readonly IOtpServiceFactory _tpServiceFactory;
        private readonly IAuthenticationRepository _oAuthRepository;
        private readonly IMfaAuditService _auditService;
        private readonly IMfaManagementService _mfaManagementService;
        private readonly IMfaAttemptGate _mfaAttemptGate;

        public MfaAuthorizationService(ILogger<MfaAuthorizationService> logger,
                                       IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
                                       IOtpServiceFactory tpServiceFactory,
                                       IAuthenticationRepository oAuthRepository,
                                       IMfaAuditService auditService,
                                       IMfaManagementService mfaManagementService,
                                       IMfaAttemptGate mfaAttemptGate)
        {
            _mfaManagementService = mfaManagementService;
            _mfaAttemptGate = mfaAttemptGate;
            _logger = logger;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _tpServiceFactory = tpServiceFactory;
            _oAuthRepository = oAuthRepository;
            _auditService = auditService;
        }

        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, IdentityConfiguration authenticationConfiguration, User? user = null)
        {
            var otpService = _tpServiceFactory.GetOTPService(request.MfaType);

            if (user != null
                && user.LockoutUntilUtc.HasValue
                && user.LockoutUntilUtc.Value > DateTime.UtcNow)
            {
                return new TokenResponse
                {
                    Error = OAuthError.AccountLocked,
                    ErrorDescription = "Account is temporarily locked due to failed authentication attempts",
                    StatusCode = 423
                };
            }

            // The challenge owner is read first, so the code is counted against that account before
            // it is checked: a burst of parallel codes gets no more checks than the lock threshold.
            var owner = await _mfaManagementService.GetChallengeOwnerAsync(request.MfaId ?? string.Empty);
            if (string.IsNullOrWhiteSpace(owner))
            {
                return new TokenResponse { Error = OAuthError.MfaInvalidCode, ErrorDescription = "Mfa code is not valid", StatusCode = 401 };
            }

            OtpVerificationResponse? response = null;
            var attempt = await _mfaAttemptGate.CheckAsync(owner, authenticationConfiguration, async () =>
            {
                response = await otpService.VerifyAsync(new VerifyOtpRequest { AuthType = request.MfaType, MfaId = request.MfaId, VerificationCode = request.Code, IsFromTokenCall = true });
                return response.IsValid && string.Equals(response.UserId, owner, StringComparison.Ordinal);
            });

            if (attempt.IsValid && response != null)
            {
                // The verified mfa_id is the only authoritative identity for this MFA session.
                // A caller-supplied user (resolved upstream from a request-body username) must
                // never select the account: honoring it let a caller pair their own valid
                // mfa_id/code with a different account's username and mint that victim's tokens.
                if (string.IsNullOrWhiteSpace(response.UserId))
                {
                    return new TokenResponse { Error = "invalid_request", ErrorDescription = "User not found for mfa session", StatusCode = 400 };
                }

                if (user != null && !string.Equals(user.ItemId, response.UserId, StringComparison.Ordinal))
                {
                    _logger.LogWarning(
                        "MFA identity mismatch: supplied user {SuppliedUserId} does not own mfa session user {SessionUserId}",
                        user.ItemId, response.UserId);
                    return new TokenResponse { Error = "invalid_request", ErrorDescription = "User not found for mfa session", StatusCode = 400 };
                }

                user ??= await _oAuthRepository.GetUserByIdAsync(response.UserId);
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

                // The mfa leg carries no organization of its own: the login that issued the challenge
                // returns before it persists one, and the mfa session stores only the user id. Resolve
                // it here, from the session user, exactly as PasswordAuthenticationService and
                // SocialAuthorizationServiceBase do for their own legs. Leaving it null let the request
                // fall through to the "default" bucket in AuthorizationClaimsResolver, so an org-scoped
                // account completed its login with organization_id "default" and no roles or
                // permissions at all — while the same account logging in without mfa got the right ones.
                request.OrganizationId = OrganizationAccessResolver.ResolveSignInOrganizationId(user, request.OrganizationId);

                var tokenResponse = await _oAuthJwtAccessTokenManager.ManageTokenAsync(request, authenticationConfiguration, user);
                if (string.IsNullOrWhiteSpace(tokenResponse.Error))
                {
                    // The counter reset and the organization stickiness the password leg writes
                    // separately are collected into one partial update here: this runs on every
                    // successful mfa login, so a second round trip would be paid on each one.
                    var postLoginUpdates = new Dictionary<string, object>();

                    if (user.FailedLoginCount > 0
                        || user.LastFailedLoginUtc.HasValue
                        || user.FailedMfaCount > 0
                        || user.LastFailedMfaUtc.HasValue
                        || user.LockoutUntilUtc.HasValue)
                    {
                        postLoginUpdates[nameof(User.FailedLoginCount)] = 0;
                        postLoginUpdates[nameof(User.LastFailedLoginUtc)] = null!;
                        postLoginUpdates[nameof(User.FailedMfaCount)] = 0;
                        postLoginUpdates[nameof(User.LastFailedMfaUtc)] = null!;
                        postLoginUpdates[nameof(User.LockoutUntilUtc)] = null!;
                        postLoginUpdates[nameof(User.LockoutCount)] = 0;
                    }

                    // Same stickiness the password leg persists, so the organization this account
                    // just signed into is the one its next login resolves to by default. Without it
                    // an mfa user's LastUsedOrganizationId would never advance.
                    if (!string.IsNullOrWhiteSpace(request.OrganizationId)
                        && !string.Equals(user.LastUsedOrganizationId, request.OrganizationId, StringComparison.OrdinalIgnoreCase))
                    {
                        postLoginUpdates[nameof(User.LastUsedOrganizationId)] = request.OrganizationId;
                    }

                    if (postLoginUpdates.Count > 0)
                    {
                        postLoginUpdates[nameof(User.LastUpdatedDate)] = DateTime.UtcNow;
                        postLoginUpdates[nameof(User.LastUpdatedBy)] = user.ItemId;
                        await _oAuthRepository.UpdatePartialAsync<User>(user.ItemId, postLoginUpdates);
                    }
                }

                await _auditService.WriteAsync(new MfaAuditEvent
                {
                    EventType = "mfa_verification_success",
                    UserId = user.ItemId,
                    ClientId = request.ClientId,
                    MfaType = request.MfaType,
                    Status = IdpConstants.StatusSuccess
                });

                return tokenResponse;
            }

            var justLocked = attempt.IsLocked;

            await _auditService.WriteAsync(new MfaAuditEvent
            {
                EventType = justLocked ? LoginAuditEvents.MfaAccountLocked : LoginAuditEvents.MfaVerificationFailure,
                UserId = owner,
                ClientId = request.ClientId,
                MfaType = request.MfaType,
                Status = IdpConstants.StatusFailure,
                Severity = IdpConstants.SeverityWarn
            });

            if (justLocked)
            {
                return new TokenResponse
                {
                    Error = OAuthError.AccountLocked,
                    ErrorDescription = "Account is temporarily locked due to failed authentication attempts",
                    StatusCode = 423
                };
            }

            return new TokenResponse { Error = OAuthError.MfaInvalidCode, ErrorDescription = "Mfa code is not valid", StatusCode = 401 };
        }
    }
}
