using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.Auth.OAuth
{
    public class PasswordAuthenticationServiceTests
    {
        private readonly Mock<IOAuthJwtAccessTokenManager> _tokenManager = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICryptoService> _crypto = new();
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IAuthenticationDomainService> _authDomain = new();
        private readonly Mock<IAccountService> _account = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();
        private readonly Mock<IUnknownUserLoginGuard> _guard = XUnitTest.TestSupport.UnknownLoginGuardStub.Create();

        public PasswordAuthenticationServiceTests()
        {
            _repo.Setup(r => r.TryReserveLoginAttemptAsync("u1", It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new User { ItemId = "u1", FailedLoginCount = 1 });
            _repo.Setup(r => r.ReleaseLoginAttemptAsync("u1", It.IsAny<DateTime>())).ReturnsAsync(true);
        }

        private PasswordAuthenticationService Create() =>
            new(NullLogger<PasswordAuthenticationService>.Instance, _tokenManager.Object, _crypto.Object, _repo.Object,
                new PasswordCredentialVerifier(NullLogger<PasswordCredentialVerifier>.Instance, _tenants.Object,
                    _repo.Object, _account.Object, _activity.Object, _authDomain.Object), _guard.Object);

        private static TokenRequest Request(string user = "jane", string pass = "secret", string? org = null) => new()
        {
            GrantType = GrantTypes.Password,
            Username = user,
            Password = pass,
            OrganizationId = org
        };

        private static IdentityConfiguration Config() => new();

        private static User ActiveUser(string hash) => new()
        {
            ItemId = "u1",
            Active = true,
            IsVerified = true,
            Password = hash,
            OrganizationIds = new List<string> { "default" }
        };

        [Fact]
        public async Task Authenticate_UserNotFound_ReturnsGenericInvalid()
        {
            _repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>())).ReturnsAsync((User)null!);

            var result = await Create().AuthenticateAsync(Request(), Config());

            result.Error.Should().Be(OAuthError.InValidUseNamePassword);
            result.StatusCode.Should().Be(401);
        }

        [Fact]
        public async Task Authenticate_InactiveUser_ReturnsGenericInvalid()
        {
            _repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>()))
                .ReturnsAsync(new User { ItemId = "u1", Active = false, IsVerified = true });

            var result = await Create().AuthenticateAsync(Request(), Config());

            result.Error.Should().Be(OAuthError.InValidUseNamePassword);
        }

        [Fact]
        public async Task Authenticate_NotVerifiedUser_ReturnsGenericInvalid()
        {
            _repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>()))
                .ReturnsAsync(new User { ItemId = "u1", Active = true, IsVerified = false });

            var result = await Create().AuthenticateAsync(Request(), Config());

            result.Error.Should().Be(OAuthError.InValidUseNamePassword);
        }

        [Fact]
        public async Task Authenticate_LockedUser_Returns423()
        {
            _repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>()))
                .ReturnsAsync(new User
                {
                    ItemId = "u1", Active = true, IsVerified = true,
                    LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10)
                });

            var result = await Create().AuthenticateAsync(Request(), Config());

            result.Error.Should().Be(OAuthError.AccountLocked);
            result.StatusCode.Should().Be(423);
        }

        private static TokenRequest RequestWithHttp(string user = "jane", string pass = "secret")
        {
            var req = Request(user, pass);
            req.Request = new DefaultHttpContext().Request;
            return req;
        }

        [Fact]
        public async Task Authenticate_LockedUser_WithHttpContext_DispatchesTimelineEvent()
        {
            _repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>()))
                .ReturnsAsync(new User
                {
                    ItemId = "u1", Active = true, IsVerified = true,
                    LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10)
                });

            var result = await Create().AuthenticateAsync(RequestWithHttp(), Config());

            result.StatusCode.Should().Be(423);
            _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(
                e => e.Event == "failed_login_account_locked" && e.UserId == "u1")), Times.Once);
        }

        [Fact]
        public async Task Authenticate_WrongPassword_TriggersLockout_WithHttpContext_DispatchesLockedTimelineEvent()
        {
            var service = Create();
            var hash = service.HashPassword("correct");
            _repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>())).ReturnsAsync(ActiveUser(hash));
            var lockedUser = new User { ItemId = "u1", LockoutUntilUtc = DateTime.UtcNow.AddMinutes(15) };
            _repo.Setup(r => r.TryReserveLoginAttemptAsync("u1", It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new User { ItemId = "u1", FailedLoginCount = 5 });
            _repo.Setup(r => r.LockAccountIfLoginThresholdReachedAsync("u1", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(lockedUser);
            _account.Setup(a => a.SendAccountLockedNotificationAsync(lockedUser, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

            var result = await service.AuthenticateAsync(RequestWithHttp(pass: "wrong"), Config());

            result.StatusCode.Should().Be(401);
            _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(
                e => e.Event == "failed_login_and_account_locked")), Times.Once);
        }

        [Fact]
        public async Task Authenticate_WrongPassword_NoLockout_WithHttpContext_DispatchesInvalidPasswordEvent()
        {
            var service = Create();
            var hash = service.HashPassword("correct");
            _repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>())).ReturnsAsync(ActiveUser(hash));

            var result = await service.AuthenticateAsync(RequestWithHttp(pass: "wrong"), Config());

            result.StatusCode.Should().Be(401);
            _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(
                e => e.Event == "failed_login_invalid_password")), Times.Once);
        }

        [Fact]
        public async Task Authenticate_WrongPassword_Returns401_AndIncrementsFailure()
        {
            var service = Create();
            var hash = service.HashPassword("correct");
            _repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>())).ReturnsAsync(ActiveUser(hash));

            var result = await service.AuthenticateAsync(Request(pass: "wrong"), Config());

            result.Error.Should().Be(OAuthError.InValidUseNamePassword);
            result.StatusCode.Should().Be(401);
            _repo.Verify(r => r.TryReserveLoginAttemptAsync("u1", It.IsAny<int>(), It.IsAny<DateTime>()), Times.Once);
            _repo.Verify(r => r.ReleaseLoginAttemptAsync(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task Authenticate_WrongPassword_TriggersLockout_SendsNotification()
        {
            var service = Create();
            var hash = service.HashPassword("correct");
            _repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>())).ReturnsAsync(ActiveUser(hash));
            var lockedUser = new User { ItemId = "u1", LockoutUntilUtc = DateTime.UtcNow.AddMinutes(15) };
            _repo.Setup(r => r.TryReserveLoginAttemptAsync("u1", It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new User { ItemId = "u1", FailedLoginCount = 5 });
            _repo.Setup(r => r.LockAccountIfLoginThresholdReachedAsync("u1", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(lockedUser);
            _account.Setup(a => a.SendAccountLockedNotificationAsync(lockedUser, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

            var result = await service.AuthenticateAsync(Request(pass: "wrong"), Config());

            result.Error.Should().Be(OAuthError.InValidUseNamePassword);
            _account.Verify(a => a.SendAccountLockedNotificationAsync(lockedUser, It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task Authenticate_CorrectPassword_ReturnsToken()
        {
            var service = Create();
            var hash = service.HashPassword("secret");
            _repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>())).ReturnsAsync(ActiveUser(hash));
            _tokenManager.Setup(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "tok" });

            var result = await service.AuthenticateAsync(Request(), Config());

            result.AccessToken.Should().Be("tok");
        }

        [Fact]
        public async Task Authenticate_CorrectPassword_ResetsFailureCounters_WhenPresent()
        {
            var service = Create();
            var hash = service.HashPassword("secret");
            var user = ActiveUser(hash);
            user.FailedLoginCount = 2;
            _repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>())).ReturnsAsync(user);
            _tokenManager.Setup(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "tok" });
            _repo.Setup(r => r.UpdatePartialAsync<User>("u1", It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            await service.AuthenticateAsync(Request(), Config());

            _repo.Verify(r => r.UpdatePartialAsync<User>("u1", It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()), Times.AtLeastOnce);
        }

        [Fact]
        public void VerifyPassword_ReturnsFalse_ForEmptyOrInvalidHash()
        {
            var service = Create();
            service.VerifyPassword("", "hash").Should().BeFalse();
            service.VerifyPassword("secret", "").Should().BeFalse();
            service.VerifyPassword("secret", "not-a-bcrypt-hash").Should().BeFalse();
        }

        [Fact]
        public void HashPassword_And_VerifyPassword_RoundTrip()
        {
            var service = Create();
            var hash = service.HashPassword("secret", "salt-1");
            service.VerifyPassword("secret", hash, "salt-1").Should().BeTrue();
            service.VerifyPassword("secret", hash).Should().BeFalse(); // salt mismatch
        }
    
        // ---------- Pentest M6: no account is told apart from a wrong password ----------

        public static TheoryData<string, bool, bool, UserLifecycleStatus> NoUsableAccount => new()
        {
            { "missing", false, false, UserLifecycleStatus.Active },
            { "pending", false, false, UserLifecycleStatus.PendingVerification },
            { "disabled", false, true, UserLifecycleStatus.Disabled },
            { "unverified", true, false, UserLifecycleStatus.Active },
        };

        [Theory]
        [MemberData(nameof(NoUsableAccount))]
        public async Task Authenticate_NoUsableAccount_SameAnswerAsWrongPassword_AndRunsTheDummyCheck(
            string kind, bool active, bool verified, UserLifecycleStatus status)
        {
            _repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>()))
                .ReturnsAsync(kind == "missing" ? null! : new User { ItemId = "x", Active = active, IsVerified = verified, Status = status, Password = "h" });
            var noAccount = await Create().AuthenticateAsync(Request(pass: "guess"), Config());

            _repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>()))
                .ReturnsAsync(ActiveUser(BCrypt.Net.BCrypt.HashPassword("secret", 4)));
            var wrongPassword = await Create().AuthenticateAsync(Request(pass: "guess"), Config());

            (noAccount.StatusCode, noAccount.Error, noAccount.ErrorDescription)
                .Should().Be((wrongPassword.StatusCode, wrongPassword.Error, wrongPassword.ErrorDescription));
            noAccount.ErrorDescription.Should().Be("Invalid username or password");
            _guard.Verify(g => g.FailAsync("jane", "guess", null, It.IsAny<IdentityConfiguration>()), Times.Once,
                "the BCrypt-equivalent check and the failure count run for a name with no usable account");
        }

        [Fact]
        public async Task Authenticate_NoUsableAccount_NoSlotLeft_AnswersLockedLikeARealAccount()
        {
            _repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>())).ReturnsAsync((User)null!);
            _guard.Setup(g => g.FailAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IdentityConfiguration>()))
                .ReturnsAsync(new UnknownUserLoginAttempt(false, false));

            var result = await Create().AuthenticateAsync(Request(), Config());

            result.StatusCode.Should().Be(StatusCodes.Status423Locked);
            result.Error.Should().Be(OAuthError.AccountLocked);
            result.ErrorDescription.Should().Be(PasswordCredentialVerifier.LockedDescription);
        }

        [Fact]
        public async Task Authenticate_AccountWithoutPassword_StillCostsABcryptCheck()
        {
            var user = ActiveUser(string.Empty); // social-only account
            _repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>())).ReturnsAsync(user);
            var bcrypt = BCrypt.Net.BCrypt.HashPassword("x");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            BCrypt.Net.BCrypt.Verify("y", bcrypt);
            var oneCheck = sw.Elapsed;

            sw.Restart();
            var result = await Create().AuthenticateAsync(Request(), Config());
            sw.Stop();

            result.Error.Should().Be(OAuthError.InValidUseNamePassword);
            sw.Elapsed.Should().BeGreaterThan(oneCheck / 3);
        }
}
}
