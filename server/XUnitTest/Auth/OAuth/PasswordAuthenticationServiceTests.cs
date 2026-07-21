using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

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

        private PasswordAuthenticationService Create() =>
            new(NullLogger<PasswordAuthenticationService>.Instance, _tokenManager.Object, _tenants.Object,
                _crypto.Object, _repo.Object, _authDomain.Object, _account.Object, _activity.Object);

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

        [Fact]
        public async Task Authenticate_WrongPassword_Returns401_AndIncrementsFailure()
        {
            var service = Create();
            var hash = service.HashPassword("correct");
            _repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>())).ReturnsAsync(ActiveUser(hash));
            _repo.Setup(r => r.IncrementFailedLoginAndApplyLockoutAsync("u1", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new User { ItemId = "u1" });

            var result = await service.AuthenticateAsync(Request(pass: "wrong"), Config());

            result.Error.Should().Be(OAuthError.InValidUseNamePassword);
            result.StatusCode.Should().Be(401);
            _repo.Verify(r => r.IncrementFailedLoginAndApplyLockoutAsync("u1", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task Authenticate_WrongPassword_TriggersLockout_SendsNotification()
        {
            var service = Create();
            var hash = service.HashPassword("correct");
            _repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>())).ReturnsAsync(ActiveUser(hash));
            var lockedUser = new User { ItemId = "u1", LockoutUntilUtc = DateTime.UtcNow.AddMinutes(15) };
            _repo.Setup(r => r.IncrementFailedLoginAndApplyLockoutAsync("u1", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
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
    }
}
