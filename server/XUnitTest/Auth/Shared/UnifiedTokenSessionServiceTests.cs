using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Iam.DomainService.Entities;

namespace XUnitTest.Auth.Shared
{
    public class UnifiedTokenSessionServiceTests
    {
        [Fact]
        public async Task CreateOrRotateRefreshToken_RevokesOldTokenInsteadOfDeleting()
        {
            var cache = new Mock<ICacheClient>();
            var authDomain = new Mock<IAuthenticationDomainService>();
            var refreshRepo = new Mock<IRefreshTokenRepository>();

            refreshRepo.Setup(r => r.CreateAsync(It.IsAny<Idp.DomainService.Oidc.Contracts.RefreshTokenModel>()))
                .ReturnsAsync("new-token");
            refreshRepo.Setup(r => r.RevokeByTokenIdAsync("old-token", "superseded_by_rotation"))
                .ReturnsAsync(true);

            var service = new UnifiedTokenSessionService(
                cache.Object,
                authDomain.Object,
                refreshRepo.Object);

            var tenant = new Tenant
            {
                TenantId = "tenant-1",
                DbConnectionString = string.Empty,
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow },
                Applications = new List<Applications>()
            };

            var user = new User
            {
                ItemId = "user-1",
                TokenVersion = 1,
                UserName = "user-1",
                Email = "user-1@test.local"
            };

            var oldCache = new RefreshTokenCache
            {
                RefreshToken = "old-token",
                TenantId = "tenant-1",
                ClientId = "client-1",
                SessionId = "session-1",
                UserId = "user-1",
                IssuedUtc = DateTime.UtcNow.AddMinutes(-30),
                ExpiresUtc = DateTime.UtcNow,
                AbsoluteExpiresUtc = DateTime.UtcNow.AddDays(7),
                IpAddresses = "127.0.0.1"
            };

            var tokenRequest = new TokenRequest
            {
                ClientId = "client-1",
                GrantType = "refresh_token",
                Scope = "openid"
            };

            var config = new IdentityConfiguration
            {
                ItemId = MongoDB.Bson.ObjectId.GenerateNewId(),
                RefreshTokenValidForNumberMinutes = 30,
                AbsoluteRefreshTokenValidForNumberMinutes = 10080
            };

            authDomain.Setup(a => a.GetDeviceInfo(It.IsAny<string>())).Returns(new Iam.DomainService.Dtos.DeviceInformation { Device = "Test" });
            authDomain.Setup(a => a.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);

            var result = await service.CreateOrRotateRefreshToken(
                "old-token",
                oldCache,
                tokenRequest,
                config,
                tenant,
                user,
                new[] { "127.0.0.1" },
                impersoanted: false);

            result.RefreshToken.Should().NotBeNullOrEmpty();
            refreshRepo.Verify(r => r.RevokeByTokenIdAsync("old-token", "superseded_by_rotation"), Times.Once);
            refreshRepo.Verify(r => r.DeleteAsync(It.IsAny<string>()), Times.Never);
            cache.Verify(c => c.RemoveKeyAsync("old-token"), Times.Once);
        }
    }
}
