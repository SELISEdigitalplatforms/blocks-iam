using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.Services;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.DisabledOrganizations
{
    /// <summary>
    /// Spec #668 Q1 on the MFA leg: the mint writes the organization it issued for back to the request (the refresh
    /// token's hint), while last-used keeps following the sign-in pre-fill, as on the password and social legs.
    /// </summary>
    public class MfaLastUsedOrganizationTests
    {
        private readonly Mock<IOAuthJwtAccessTokenManager> _tokenManager = new();
        private readonly Mock<IOtpServiceFactory> _otpFactory = new();
        private readonly Mock<IOtpService> _otpService = new();
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IMfaAuditService> _audit = new();
        private readonly Mock<IMfaManagementService> _mfaManagement = new();

        public MfaLastUsedOrganizationTests()
        {
            _mfaManagement.Setup(m => m.GetChallengeOwnerAsync("mfa-1")).ReturnsAsync("u1");
            _repo.Setup(r => r.TryReserveMfaAttemptAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync((string id, int _, DateTime _) => new User { ItemId = id, FailedMfaCount = 1 });
            _repo.Setup(r => r.ReleaseMfaAttemptAsync(It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(true);
            _otpFactory.Setup(f => f.GetOTPService(It.IsAny<UserMfaType>())).Returns(_otpService.Object);
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = true, UserId = "u1" });
            _audit.Setup(a => a.WriteAsync(It.IsAny<MfaAuditEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        }

        private MfaAuthorizationService Create() =>
            new(NullLogger<MfaAuthorizationService>.Instance, _tokenManager.Object,
                _otpFactory.Object, _repo.Object, _audit.Object, _mfaManagement.Object,
                new Authentication.DomainService.Authentication.MfaAttemptGate(_repo.Object));

        [Fact]
        public async Task Q1_MfaLogin_LastUsedFollowsThePreFill_NotTheWrittenBackOrganization()
        {
            // Memberships [D, A], nothing remembered: the pre-fill is D, the mint falls back to A and writes A back.
            var user = new User { ItemId = "u1", IsMfaVerified = true, OrganizationIds = ["org-d", "org-a"] };
            _repo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);
            _tokenManager.Setup(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), user, It.IsAny<StateInfo?>()))
                .Callback((TokenRequest request, IdentityConfiguration _, User _, StateInfo? _) => request.OrganizationId = "org-a")
                .ReturnsAsync(new TokenResponse { AccessToken = "tok", StatusCode = 200 });

            await Create().AuthenticateAsync(
                new TokenRequest { MfaType = UserMfaType.Email, MfaId = "mfa-1", Code = "123456", ClientId = "client-1" },
                new IdentityConfiguration());

            _repo.Verify(r => r.UpdatePartialAsync<User>("u1",
                It.Is<Dictionary<string, object>>(d =>
                    d.ContainsKey(nameof(User.LastUsedOrganizationId))
                    && (string)d[nameof(User.LastUsedOrganizationId)] == "org-d"),
                It.IsAny<string>()), Times.Once);
        }
    }
}
