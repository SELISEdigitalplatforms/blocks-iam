using Authentication.DomainService.Authentication;
using global::Authentication.DomainService.Services;
using FluentAssertions;
using global::Iam.DomainService.Entities;
using global::Iam.DomainService.Services;
using global::Mfa.DomainService.Services;
using global::Mfa.DomainService.Shared;
using global::Mfa.DomainService.Entities;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Moq;
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.Auth
{
    public class MfaDisableServiceTests
    {
        private const string UserId = "u1";

        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IPasswordCredentialVerifier> _password = new();
        private readonly Mock<IMfaManagementService> _mfa = new();
        private readonly Mock<IMfaPolicyService> _policy = new();
        private readonly Mock<IUserSessionTerminator> _sessions = new();
        private readonly PassThroughMfaAttemptGate _gate = new();

        private static readonly MfaDisableRequest FullProof = new() { Password = "pw", MfaId = "mfa-1", VerificationCode = "123456" };

        public MfaDisableServiceTests()
        {
            _repo.Setup(r => r.GetUserByIdAsync(UserId))
                .ReturnsAsync(new User { ItemId = UserId, MfaEnabled = true, UserMfaType = UserMfaType.TOTP });
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _policy.Setup(p => p.EvaluateAsync(It.IsAny<User>(), null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MfaPolicyDecision { Required = true, CanUserDisable = true });
            _password.Setup(p => p.VerifyPasswordAsync(It.IsAny<User>(), "pw", It.IsAny<IdentityConfiguration>(), null, It.IsAny<string?>()))
                .ReturnsAsync(PasswordVerificationResult.Success());
            _mfa.Setup(m => m.GetChallengeOwnerAsync("mfa-1")).ReturnsAsync(UserId);
            _mfa.Setup(m => m.VerifyOTPAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsSuccess = true, IsValid = true, UserId = UserId });
            _mfa.Setup(m => m.DisableUserMfa(It.IsAny<DisableUserMfaRequest>())).ReturnsAsync(new BaseResponse { IsSuccess = true });
        }

        private MfaDisableService Create() => new(_repo.Object, _password.Object, _mfa.Object, _policy.Object, _sessions.Object, _gate);

        [Fact]
        public async Task SessionAlone_IsNotEnough_ProofIsRequired()
        {
            var result = await Create().DisableAsync(UserId, new MfaDisableRequest());

            result.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            _mfa.Verify(m => m.DisableUserMfa(It.IsAny<DisableUserMfaRequest>()), Times.Never);
        }

        [Fact]
        public async Task TenantPolicyThatForbidsOptOut_IsFollowed()
        {
            _policy.Setup(p => p.EvaluateAsync(It.IsAny<User>(), null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MfaPolicyDecision { Required = true, CanUserDisable = false });

            var result = await Create().DisableAsync(UserId, FullProof);

            result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            result.Error.Should().Be(MfaDisableService.DisableNotAllowed);
            _mfa.Verify(m => m.DisableUserMfa(It.IsAny<DisableUserMfaRequest>()), Times.Never);
        }

        [Fact]
        public async Task WrongPassword_KeepsMfaOn()
        {
            _password.Setup(p => p.VerifyPasswordAsync(It.IsAny<User>(), "pw", It.IsAny<IdentityConfiguration>(), null, It.IsAny<string?>()))
                .ReturnsAsync(new PasswordVerificationResult { Error = "invalid_username_password", StatusCode = StatusCodes.Status401Unauthorized });

            var result = await Create().DisableAsync(UserId, FullProof);

            result.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            _mfa.Verify(m => m.VerifyOTPAsync(It.IsAny<VerifyOtpRequest>()), Times.Never);
            _mfa.Verify(m => m.DisableUserMfa(It.IsAny<DisableUserMfaRequest>()), Times.Never);
        }

        [Fact]
        public async Task ChallengeOfAnotherUser_IsRefused_AndCountsAsAWrongCode()
        {
            _mfa.Setup(m => m.GetChallengeOwnerAsync("mfa-1")).ReturnsAsync("someone-else");

            var result = await Create().DisableAsync(UserId, FullProof);

            result.Error.Should().Be(MfaDisableService.InvalidCode);
            _mfa.Verify(m => m.VerifyOTPAsync(It.IsAny<VerifyOtpRequest>()), Times.Never);
            _gate.LastUserId.Should().Be(UserId);
            _mfa.Verify(m => m.DisableUserMfa(It.IsAny<DisableUserMfaRequest>()), Times.Never);
        }

        [Fact]
        public async Task WrongCode_ThatReachesTheLimit_ReturnsLocked()
        {
            _mfa.Setup(m => m.VerifyOTPAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsSuccess = true, IsValid = false });
            _gate.LockOnWrongCode = true;

            var result = await Create().DisableAsync(UserId, FullProof);

            result.StatusCode.Should().Be(StatusCodes.Status423Locked);
            _mfa.Verify(m => m.DisableUserMfa(It.IsAny<DisableUserMfaRequest>()), Times.Never);
        }

        [Fact]
        public async Task FullProof_ChecksTheCurrentMethod_DisablesMfa_AndEndsAllSessions()
        {
            var result = await Create().DisableAsync(UserId, FullProof);

            result.Succeeded.Should().BeTrue();
            result.PreviousMethod.Should().Be(UserMfaType.TOTP);
            // The method comes from the user record, and the check must not re-enable MFA.
            _mfa.Verify(m => m.VerifyOTPAsync(It.Is<VerifyOtpRequest>(r =>
                r.AuthType == UserMfaType.TOTP && r.IsFromTokenCall && r.MfaId == "mfa-1")), Times.Once);
            _mfa.Verify(m => m.DisableUserMfa(It.Is<DisableUserMfaRequest>(r => r.UserId == UserId)), Times.Once);
            _sessions.Verify(s => s.EndAllSessionsAsync(UserId, "mfa_disabled"), Times.Once);
        }
    }
}
