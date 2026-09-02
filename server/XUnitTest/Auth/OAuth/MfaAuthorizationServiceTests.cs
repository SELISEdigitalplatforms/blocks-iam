using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.Services;
using FluentAssertions;
using Iam.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Iam.DomainService.Utilities;

namespace XUnitTest.Auth.OAuth
{
    public class MfaAuthorizationServiceTests
    {
        private readonly Mock<IOAuthJwtAccessTokenManager> _tokenManager = new();
        private readonly Mock<IOtpServiceFactory> _otpFactory = new();
        private readonly Mock<IOtpService> _otpService = new();
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IMfaAuditService> _audit = new();

        public MfaAuthorizationServiceTests()
        {
            _otpFactory.Setup(f => f.GetOTPService(It.IsAny<UserMfaType>())).Returns(_otpService.Object);
            _audit.Setup(a => a.WriteAsync(It.IsAny<MfaAuditEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        }

        private MfaAuthorizationService Create() =>
            new(NullLogger<MfaAuthorizationService>.Instance, _tokenManager.Object,
                _otpFactory.Object, _repo.Object, _audit.Object);

        private static TokenRequest Request() => new()
        {
            MfaType = UserMfaType.Email,
            MfaId = "mfa-1",
            Code = "123456",
            ClientId = "client-1"
        };

        private static IdentityConfiguration Config() => new();

        [Fact]
        public async Task Authenticate_UserAlreadyLocked_Returns423()
        {
            var user = new User { ItemId = "u1", LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10) };

            var result = await Create().AuthenticateAsync(Request(), Config(), user);

            result.Error.Should().Be(OAuthError.AccountLocked);
            result.StatusCode.Should().Be(423);
            _otpService.Verify(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()), Times.Never);
        }

        [Fact]
        public async Task Authenticate_ValidOtp_UserNotFound_ReturnsInvalidRequest()
        {
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = true, UserId = "missing" });
            _repo.Setup(r => r.GetUserByIdAsync("missing")).ReturnsAsync((User?)null!);

            var result = await Create().AuthenticateAsync(Request(), Config());

            result.Error.Should().Be("invalid_request");
            result.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task Authenticate_ValidOtp_SuppliedUserDiffersFromMfaSessionUser_ReturnsInvalidRequest_AndDoesNotMintToken()
        {
            // Attacker pairs their own valid mfa_id/code (session user "attacker") with a victim
            // account object resolved upstream from a request-body username. The mfa session user
            // must win and the request must be rejected — never a token for the victim.
            var victim = new User { ItemId = "victim", IsMfaVerified = true };
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = true, UserId = "attacker" });

            var result = await Create().AuthenticateAsync(Request(), Config(), victim);

            result.Error.Should().Be("invalid_request");
            result.StatusCode.Should().Be(400);
            _tokenManager.Verify(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()), Times.Never);
        }

        [Fact]
        public async Task Authenticate_ValidOtp_NoSuppliedUser_ResolvesFromMfaSession_AndMintsToken()
        {
            // Mirrors the embedded flow after the fix: no user is passed in, so the account is
            // resolved solely from the verified mfa_id. Guards the prod regression where a wrong
            // upstream user object had blocked a legitimate, MFA-verified login.
            var sessionUser = new User { ItemId = "u1", IsMfaVerified = true };
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = true, UserId = "u1" });
            _repo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(sessionUser);
            _tokenManager.Setup(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), sessionUser, It.IsAny<StateInfo?>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "tok", StatusCode = 200 });

            var result = await Create().AuthenticateAsync(Request(), Config());

            result.AccessToken.Should().Be("tok");
        }

        [Fact]
        public async Task Authenticate_ValidOtp_BlankSessionUserId_ReturnsInvalidRequest()
        {
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = true, UserId = null });

            var result = await Create().AuthenticateAsync(Request(), Config());

            result.Error.Should().Be("invalid_request");
            result.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task Authenticate_ValidOtp_UserNotMfaVerified_ReturnsUnverified()
        {
            var user = new User { ItemId = "u1", IsMfaVerified = false };
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = true, UserId = "u1" });

            var result = await Create().AuthenticateAsync(Request(), Config(), user);

            result.Error.Should().Be("unverified_user_mfa");
            result.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task Authenticate_ValidOtp_MfaVerified_ReturnsToken_AndWritesSuccessAudit()
        {
            var user = new User { ItemId = "u1", IsMfaVerified = true };
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = true, UserId = "u1" });
            _tokenManager.Setup(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), user, It.IsAny<StateInfo?>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "tok", StatusCode = 200 });

            var result = await Create().AuthenticateAsync(Request(), Config(), user);

            result.AccessToken.Should().Be("tok");
            _audit.Verify(a => a.WriteAsync(It.Is<MfaAuditEvent>(e => e.EventType == "mfa_verification_success"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Authenticate_ValidOtp_ResetsFailedCounters_WhenPresent()
        {
            var user = new User { ItemId = "u1", IsMfaVerified = true, FailedMfaCount = 3, FailedLoginCount = 1 };
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = true, UserId = "u1" });
            _tokenManager.Setup(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), user, It.IsAny<StateInfo?>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "tok" });
            _repo.Setup(r => r.UpdatePartialAsync<User>("u1", It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            await Create().AuthenticateAsync(Request(), Config(), user);

            _repo.Verify(r => r.UpdatePartialAsync<User>("u1", It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task Authenticate_InvalidOtp_NotLocked_Returns401_AndWritesFailureAudit()
        {
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = false, UserId = "u1" });
            _repo.Setup(r => r.IncrementFailedMfaAndApplyLockoutAsync("u1", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new User { ItemId = "u1", FailedMfaCount = 1 });

            var result = await Create().AuthenticateAsync(Request(), Config());

            result.Error.Should().Be(OAuthError.MfaInvalidCode);
            result.StatusCode.Should().Be(401);
            _audit.Verify(a => a.WriteAsync(It.Is<MfaAuditEvent>(e => e.Status == "failure"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Authenticate_InvalidOtp_JustLocked_Returns423()
        {
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = false, UserId = "u1" });
            _repo.Setup(r => r.IncrementFailedMfaAndApplyLockoutAsync("u1", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new User { ItemId = "u1", LockoutUntilUtc = DateTime.UtcNow.AddMinutes(15) });

            var result = await Create().AuthenticateAsync(Request(), Config());

            result.Error.Should().Be(OAuthError.AccountLocked);
            result.StatusCode.Should().Be(423);
        }

        [Fact]
        public async Task Authenticate_OrgScopedUser_ScopesTokenRequestToThatOrganization()
        {
            // The regression this guards: the mfa leg used to leave OrganizationId null, so
            // AuthorizationClaimsResolver fell back to the "default" bucket. For a user whose roles
            // and permissions live only under "org-x" that produced a token with organization_id
            // "default" and no roles at all — while the same account logging in without mfa got
            // the right organization and the right claims.
            var user = new User
            {
                ItemId = "u1",
                IsMfaVerified = true,
                LastUsedOrganizationId = "org-x",
                OrganizationIds = ["org-x"],
                Roles = { ["org-x"] = ["admin"] },
                Permissions = { ["org-x"] = ["blocks-iam::users::read"] }
            };
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = true, UserId = "u1" });
            _repo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);

            TokenRequest? captured = null;
            _tokenManager.Setup(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), user, It.IsAny<StateInfo?>()))
                .Callback<TokenRequest, IdentityConfiguration, User, StateInfo?>((r, _, _, _) => captured = r)
                .ReturnsAsync(new TokenResponse { AccessToken = "tok", StatusCode = 200 });

            var result = await Create().AuthenticateAsync(Request(), Config());

            result.AccessToken.Should().Be("tok");
            captured!.OrganizationId.Should().Be("org-x");
        }

        [Fact]
        public async Task Authenticate_OrgGrantedOnlyThroughRoles_StillScopesToThatOrganization()
        {
            // Membership is the three-way test: an organization can be granted through a role or
            // permission assignment without ever landing in OrganizationIds.
            var user = new User
            {
                ItemId = "u1",
                IsMfaVerified = true,
                LastUsedOrganizationId = "org-y",
                Roles = { ["org-y"] = ["member"] }
            };
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = true, UserId = "u1" });
            _repo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);

            TokenRequest? captured = null;
            _tokenManager.Setup(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), user, It.IsAny<StateInfo?>()))
                .Callback<TokenRequest, IdentityConfiguration, User, StateInfo?>((r, _, _, _) => captured = r)
                .ReturnsAsync(new TokenResponse { AccessToken = "tok", StatusCode = 200 });

            await Create().AuthenticateAsync(Request(), Config());

            captured!.OrganizationId.Should().Be("org-y");
        }

        [Fact]
        public async Task Authenticate_DefaultOrgUser_StillResolvesToDefault()
        {
            var user = new User
            {
                ItemId = "u1",
                IsMfaVerified = true,
                OrganizationIds = ["default"],
                Roles = { ["default"] = ["admin"] }
            };
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = true, UserId = "u1" });
            _repo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);

            TokenRequest? captured = null;
            _tokenManager.Setup(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), user, It.IsAny<StateInfo?>()))
                .Callback<TokenRequest, IdentityConfiguration, User, StateInfo?>((r, _, _, _) => captured = r)
                .ReturnsAsync(new TokenResponse { AccessToken = "tok", StatusCode = 200 });

            await Create().AuthenticateAsync(Request(), Config());

            // Still "default", and now for the right reason: the user is literally a member of it,
            // so the resolver returns it as a membership rather than as a fallback.
            captured!.OrganizationId.Should().Be(IdpConstants.DefaultOrganizationId);
        }

        [Fact]
        public async Task Authenticate_UserWithNoOrganizationAnywhere_ResolvesToNoOrg()
        {
            var user = new User { ItemId = "u1", IsMfaVerified = true };
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = true, UserId = "u1" });
            _repo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);

            TokenRequest? captured = null;
            _tokenManager.Setup(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), user, It.IsAny<StateInfo?>()))
                .Callback<TokenRequest, IdentityConfiguration, User, StateInfo?>((r, _, _, _) => captured = r)
                .ReturnsAsync(new TokenResponse { AccessToken = "tok", StatusCode = 200 });

            await Create().AuthenticateAsync(Request(), Config());

            // H5: this used to fall back to "default" -- the tenant-wide scope -- handing the widest
            // authority in the system to a user with no organization at all.
            captured!.OrganizationId.Should().Be(IdpConstants.NoOrganizationId);
        }

        [Fact]
        public async Task Authenticate_RequestedOrganizationTheUserDoesNotBelongTo_IsIgnored()
        {
            // OrganizationId is request-controlled on the grant surfaces that carry one, so the
            // resolver must never honour an organization the mfa session user has no access to.
            var user = new User
            {
                ItemId = "u1",
                IsMfaVerified = true,
                OrganizationIds = ["org-x"],
                Roles = { ["org-x"] = ["member"] }
            };
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = true, UserId = "u1" });
            _repo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);

            TokenRequest? captured = null;
            _tokenManager.Setup(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), user, It.IsAny<StateInfo?>()))
                .Callback<TokenRequest, IdentityConfiguration, User, StateInfo?>((r, _, _, _) => captured = r)
                .ReturnsAsync(new TokenResponse { AccessToken = "tok", StatusCode = 200 });

            var request = Request();
            request.OrganizationId = "someone-elses-org";

            await Create().AuthenticateAsync(request, Config());

            captured!.OrganizationId.Should().Be("org-x");
        }

        [Fact]
        public async Task Authenticate_Success_PersistsLastUsedOrganizationId_WhenItChanged()
        {
            var user = new User
            {
                ItemId = "u1",
                IsMfaVerified = true,
                LastUsedOrganizationId = null,
                OrganizationIds = ["org-x"],
                Roles = { ["org-x"] = ["admin"] }
            };
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = true, UserId = "u1" });
            _repo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);
            _tokenManager.Setup(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), user, It.IsAny<StateInfo?>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "tok", StatusCode = 200 });

            await Create().AuthenticateAsync(Request(), Config());

            _repo.Verify(r => r.UpdatePartialAsync<User>("u1",
                It.Is<Dictionary<string, object>>(d =>
                    d.ContainsKey(nameof(User.LastUsedOrganizationId))
                    && (string)d[nameof(User.LastUsedOrganizationId)] == "org-x"),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task Authenticate_FailedMfa_DoesNotPersistLastUsedOrganizationId()
        {
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = false, UserId = "u1" });
            _repo.Setup(r => r.IncrementFailedMfaAndApplyLockoutAsync("u1", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new User { ItemId = "u1", FailedMfaCount = 1 });

            await Create().AuthenticateAsync(Request(), Config());

            _repo.Verify(r => r.UpdatePartialAsync<User>(It.IsAny<string>(),
                It.Is<Dictionary<string, object>>(d => d.ContainsKey(nameof(User.LastUsedOrganizationId))),
                It.IsAny<string>()), Times.Never);
        }
    }
}
