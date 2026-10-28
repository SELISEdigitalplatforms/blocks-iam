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
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.Auth.OAuth
{
    public class MfaAuthorizationServiceTests
    {
        private readonly Mock<IOAuthJwtAccessTokenManager> _tokenManager = new();
        private readonly Mock<IOtpServiceFactory> _otpFactory = new();
        private readonly Mock<IOtpService> _otpService = new();
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IMfaAuditService> _audit = new();
        private readonly Mock<IMfaManagementService> _mfaManagement = new();

        public MfaAuthorizationServiceTests()
        {
            _mfaManagement.Setup(m => m.GetChallengeOwnerAsync("mfa-1")).ReturnsAsync("u1");
            // A free MFA slot, given back when the code is right.
            _repo.Setup(r => r.TryReserveMfaAttemptAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync((string id, int _, DateTime _) => new User { ItemId = id, FailedMfaCount = 1 });
            _repo.Setup(r => r.ReleaseMfaAttemptAsync(It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(true);
            _otpFactory.Setup(f => f.GetOTPService(It.IsAny<UserMfaType>())).Returns(_otpService.Object);
            _audit.Setup(a => a.WriteAsync(It.IsAny<MfaAuditEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        }

        private MfaAuthorizationService Create() =>
            new(NullLogger<MfaAuthorizationService>.Instance, _tokenManager.Object,
                _otpFactory.Object, _repo.Object, _audit.Object, _mfaManagement.Object,
                new Authentication.DomainService.Authentication.MfaAttemptGate(_repo.Object));

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
            _mfaManagement.Setup(m => m.GetChallengeOwnerAsync("mfa-1")).ReturnsAsync("missing");
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
            _mfaManagement.Setup(m => m.GetChallengeOwnerAsync("mfa-1")).ReturnsAsync("attacker");

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
        public async Task Authenticate_UnknownChallenge_IsRefused_AndNoCodeIsChecked()
        {
            _mfaManagement.Setup(m => m.GetChallengeOwnerAsync("mfa-1")).ReturnsAsync((string?)null);

            var result = await Create().AuthenticateAsync(Request(), Config());

            result.Error.Should().Be(OAuthError.MfaInvalidCode);
            result.StatusCode.Should().Be(401);
            _otpService.Verify(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()), Times.Never);
        }

        [Fact]
        public async Task Authenticate_ValidOtp_ForAnotherUserThanTheChallengeOwner_IsRefused()
        {
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = true, UserId = "someone-else" });

            var result = await Create().AuthenticateAsync(Request(), Config());

            result.StatusCode.Should().Be(401);
            _tokenManager.Verify(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()), Times.Never);
        }

        [Fact]
        public async Task Authenticate_ParallelWrongCodes_GetNoMoreChecksThanTheThreshold()
        {
            // The real counting contract: a slot is taken before each check, none come back for a wrong code.
            var taken = 0;
            _repo.Setup(r => r.TryReserveMfaAttemptAsync("u1", 3, It.IsAny<DateTime>()))
                .ReturnsAsync(() => Interlocked.Increment(ref taken) <= 3 ? new User { ItemId = "u1", FailedMfaCount = taken } : null);
            _otpService.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = false, UserId = "u1" });
            var config = new IdentityConfiguration { GetNumberOfWrongAttemptsToLockTheAccount = 3 };

            var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Create().AuthenticateAsync(Request(), config)));

            _otpService.Verify(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()), Times.Exactly(3));
            results.Count(r => r.StatusCode == 423).Should().Be(47);
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
            _repo.Setup(r => r.TryReserveMfaAttemptAsync("u1", It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new User { ItemId = "u1", FailedMfaCount = 100 });
            _repo.Setup(r => r.LockAccountIfMfaThresholdReachedAsync("u1", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new User { ItemId = "u1", LockoutUntilUtc = DateTime.UtcNow.AddMinutes(15) });

            var result = await Create().AuthenticateAsync(Request(), Config());

            result.Error.Should().Be(OAuthError.AccountLocked);
            result.StatusCode.Should().Be(423);
        }

        // ---- Real EmailOtpService behind the login mfa step (cache mocked) ----

        private (MfaAuthorizationService Sut, MfaAuthenticationContext Context) CreateWithRealEmailOtp()
        {
            var cache = new Mock<Blocks.Genesis.ICacheClient>();
            var context = MfaAuthenticationContext.Create("mfa-1", "u1", UserMfaType.Email);
            context.MfaCode = "004217";
            cache.Setup(c => c.KeyExistsAsync("mfa-1")).ReturnsAsync(true);
            cache.Setup(c => c.GetStringValueAsync("mfa-1")).ReturnsAsync(context.Sterilize());
            cache.Setup(c => c.RemoveKeyAsync("mfa-1")).ReturnsAsync(true);
            var email = new global::Mfa.DomainService.OTP.Services.EmailOtpService(
                cache.Object, new Mock<global::Mfa.DomainService.Configuration.IMfaConfigurationService>().Object, new Mock<Blocks.Genesis.IMessageClient>().Object);
            _otpFactory.Setup(f => f.GetOTPService(UserMfaType.Email)).Returns(email);
            return (Create(), context);
        }

        [Fact]
        public async Task Authenticate_EmailOtp_WrongCode_IsCountedAgainstTheChallengeOwner()
        {
            var (sut, _) = CreateWithRealEmailOtp();
            var request = Request();
            request.Code = "000000";
            var config = new IdentityConfiguration { GetNumberOfWrongAttemptsToLockTheAccount = 7, AccountLockDurationInMinutes = 9 };

            var result = await sut.AuthenticateAsync(request, config);

            result.StatusCode.Should().Be(401);
            _repo.Verify(r => r.TryReserveMfaAttemptAsync("u1", 7, It.IsAny<DateTime>()), Times.Once);
            _repo.Verify(r => r.ReleaseMfaAttemptAsync(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("4217")]
        [InlineData("12345")]
        public async Task Authenticate_EmailOtp_BypassInputs_AreCountedAndRefused(string code)
        {
            var (sut, _) = CreateWithRealEmailOtp();
            var request = Request();
            request.Code = code;

            var result = await sut.AuthenticateAsync(request, Config());

            result.StatusCode.Should().Be(401);
            _tokenManager.Verify(t => t.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()), Times.Never);
            _repo.Verify(r => r.TryReserveMfaAttemptAsync("u1", It.IsAny<int>(), It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task Authenticate_EmailOtp_RepeatedWrongCodes_LockThenRightCodeIsAlsoRefused()
        {
            var (sut, context) = CreateWithRealEmailOtp();
            var locked = new User { ItemId = "u1", IsMfaVerified = true };
            var attempts = 0;
            _repo.Setup(r => r.TryReserveMfaAttemptAsync("u1", It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(() =>
                {
                    if (locked.LockoutUntilUtc > DateTime.UtcNow) return null;
                    attempts++;
                    locked.FailedMfaCount = attempts;
                    return locked;
                });
            _repo.Setup(r => r.LockAccountIfMfaThresholdReachedAsync("u1", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(() =>
                {
                    locked.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(15);
                    return locked;
                });
            _repo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(() => locked);
            var config = new IdentityConfiguration { GetNumberOfWrongAttemptsToLockTheAccount = 3 };

            var wrong = Request();
            wrong.Code = "000000";
            (await sut.AuthenticateAsync(wrong, config)).StatusCode.Should().Be(401);
            (await sut.AuthenticateAsync(wrong, config)).StatusCode.Should().Be(401);
            (await sut.AuthenticateAsync(wrong, config)).StatusCode.Should().Be(423);

            // Locked: the correct code gets the same 423, with and without a resolved user.
            var right = Request();
            right.Code = context.MfaCode;
            (await sut.AuthenticateAsync(right, config, locked)).StatusCode.Should().Be(423);
            (await sut.AuthenticateAsync(right, config)).StatusCode.Should().Be(423);
            _tokenManager.Verify(t => t.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()), Times.Never);
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

            await Create().AuthenticateAsync(Request(), Config());

            _repo.Verify(r => r.UpdatePartialAsync<User>(It.IsAny<string>(),
                It.Is<Dictionary<string, object>>(d => d.ContainsKey(nameof(User.LastUsedOrganizationId))),
                It.IsAny<string>()), Times.Never);
        }
    }
}
