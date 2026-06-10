using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using BCryptNet = BCrypt.Net.BCrypt;

namespace XUnitTest.DomainService.OAuth.Services
{
    public class PasswordAuthenticationServiceTests
    {
        private readonly Mock<ILogger<PasswordAuthenticationService>> _logger = new();
        private readonly Mock<IOAuthJwtAccessTokenManager> _oAuthJwtAccessTokenManager = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICryptoService> _cryptoService = new();
        private readonly Mock<IAuthenticationRepository> _oAuthRepository = new();
        private readonly Mock<IAuthenticationDomainService> _authenticationDomainService = new();
        private readonly Mock<ICacheClient> _cacheClient = new();
        private readonly Mock<IAccountService> _accountService = new();
        private readonly PasswordAuthenticationService _service;

        public PasswordAuthenticationServiceTests()
        {
            _service = new PasswordAuthenticationService(
                _logger.Object,
                _oAuthJwtAccessTokenManager.Object,
                _tenants.Object,
                _cryptoService.Object,
                _oAuthRepository.Object,
                _authenticationDomainService.Object,
                _cacheClient.Object,
                _accountService.Object);

            _authenticationDomainService
                .Setup(x => x.GetVisitorsIpAddresses(It.IsAny<HttpContext>()))
                .Returns(new[] { "127.0.0.1" });
            _authenticationDomainService
                .Setup(x => x.GetDeviceInfo(It.IsAny<string>()))
                .Returns((DeviceInformation?)null);
            _authenticationDomainService
                .Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<UserAuthenticationTimelineEvent>()))
                .Returns(Task.CompletedTask);
        }

        private static TokenRequest BuildTokenRequest(string username, string password, string organizationId = "org-1")
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
            context.Request.Headers.UserAgent = "xunit-agent";

            return new TokenRequest
            {
                Username = username,
                Password = password,
                OrganizationId = organizationId,
                GrantType = "password",
                Request = context.Request
            };
        }

        [Fact]
        public void AuthenticationConfiguration_DefaultDailyLimit_Is500()
        {
            var config = new IdentityConfiguration();

            config.MaxLoginAttemptsPerIpPerDay.Should().Be(500);
        }

        [Fact]
        public async Task AuthenticateAsync_WithInvalidUser_ReturnsInvalidResponse()
        {
            // Arrange
            var request = BuildTokenRequest("nonexistent@example.com", "password123", "org-123");
            var authConfig = new IdentityConfiguration();

            _oAuthRepository
                .Setup(x => x.GetUserByUsernameAsync(request.Username, request.OrganizationId))
                .ReturnsAsync((User)null);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.Error.Should().NotBeNullOrEmpty();
            _oAuthRepository.Verify(x => x.GetUserByUsernameAsync(request.Username, request.OrganizationId), Times.Once);
            _oAuthJwtAccessTokenManager.Verify(
                x => x.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo>()),
                Times.Never);
        }

        [Fact]
        public async Task AuthenticateAsync_WhenDailyLimitExceeded_ReturnsIpRateLimited()
        {
            // Arrange
            var request = BuildTokenRequest("test@example.com", "password123", "org-123");
            var authConfig = new IdentityConfiguration
            {
                MaxLoginAttemptsPerIpPerHour = 100,
                MaxLoginAttemptsPerIpPerDay = 500
            };
            var user = new User
            {
                ItemId = "user-789",
                Email = "test@example.com",
                UserName = "test@example.com",
                Password = BCryptNet.HashPassword("password123"),
                Active = true,
                IsVerified = true
            };

            _oAuthRepository
                .Setup(x => x.GetUserByUsernameAsync(request.Username, request.OrganizationId))
                .ReturnsAsync(user);
            _cacheClient
                .Setup(x => x.GetStringValueAsync(It.Is<string>(k => k.StartsWith("login_ip_hourly:127.0.0.1:"))))
                .ReturnsAsync("8");
            _cacheClient
                .Setup(x => x.GetStringValueAsync(It.Is<string>(k => k.StartsWith("login_ip_daily:127.0.0.1:"))))
                .ReturnsAsync("500");

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.StatusCode.Should().Be(429);
            result.Error.Should().Be("ip_rate_limited");
            _cacheClient.Verify(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task AuthenticateAsync_WhenAccountGetsLocked_SendsAccountLockedNotification()
        {
            // Arrange
            var request = BuildTokenRequest("lockme@example.com", "wrong-password", "org-123");
            var authConfig = new IdentityConfiguration
            {
                MaxLoginAttemptsPerIpPerHour = 100,
                MaxLoginAttemptsPerIpPerDay = 500,
                GetNumberOfWrongAttemptsToLockTheAccount = 5,
                AccountLockDurationInMinutes = 5
            };

            var user = new User
            {
                ItemId = "user-locked",
                Email = "lockme@example.com",
                UserName = "lockme@example.com",
                Password = BCryptNet.HashPassword("correct-password"),
                Active = true,
                IsVerified = true
            };

            var lockoutUntil = DateTime.UtcNow.AddMinutes(5);
            var updatedUser = new User
            {
                ItemId = user.ItemId,
                Email = user.Email,
                FirstName = "Lock",
                LastName = "User",
                LockoutUntilUtc = lockoutUntil,
                Active = true,
                IsVerified = true
            };

            _oAuthRepository
                .Setup(x => x.GetUserByUsernameAsync(request.Username, request.OrganizationId))
                .ReturnsAsync(user);
            _oAuthRepository
                .Setup(x => x.IncrementFailedLoginAndApplyLockoutAsync(user.ItemId, authConfig.GetNumberOfWrongAttemptsToLockTheAccount, authConfig.AccountLockDurationInMinutes, It.IsAny<DateTime>()))
                .ReturnsAsync(updatedUser);

            _cacheClient
                .Setup(x => x.GetStringValueAsync(It.Is<string>(k => k.StartsWith("login_ip_hourly:127.0.0.1:"))))
                .ReturnsAsync("1");
            _cacheClient
                .Setup(x => x.GetStringValueAsync(It.Is<string>(k => k.StartsWith("login_ip_daily:127.0.0.1:"))))
                .ReturnsAsync("1");
            _cacheClient
                .Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            _accountService
                .Setup(x => x.SendAccountLockedNotificationAsync(It.IsAny<User>(), It.IsAny<DateTime>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.StatusCode.Should().Be(401);
            result.Error.Should().Be(OAuthError.InValidUseNamePassword);
            _accountService.Verify(x => x.SendAccountLockedNotificationAsync(
                It.Is<User>(u => u.ItemId == user.ItemId),
                It.Is<DateTime>(d => d == lockoutUntil)), Times.Once);
        }

        [Fact]
        public void HashPassword_ReturnsBcryptHash()
        {
            // Arrange
            var password = "mySecurePassword123";

            // Act
            var result = _service.HashPassword(password);

            // Assert
            result.Should().NotBeNullOrWhiteSpace();
            BCryptNet.Verify(password, result).Should().BeTrue();
        }

        [Fact]
        public void VerifyPassword_WithMatchingHash_ReturnsTrue()
        {
            // Arrange
            var password = "mySecurePassword123";
            var hash = _service.HashPassword(password);

            // Act
            var result = _service.VerifyPassword(password, hash);

            // Assert
            result.Should().BeTrue();
        }
    }
}