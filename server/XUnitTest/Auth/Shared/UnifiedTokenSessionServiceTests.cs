using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Users;

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
            var activityDispatcher = new Mock<IUserActivityDispatcher>();
            var idpSessionService = new Mock<IIdpSessionService>();
            var userRepo = new Mock<IUserRepository>();
            var httpContextAccessor = new Mock<IHttpContextAccessor>();

            refreshRepo.Setup(r => r.CreateAsync(It.IsAny<Idp.DomainService.Oidc.Contracts.RefreshTokenModel>()))
                .ReturnsAsync("new-token");
            refreshRepo.Setup(r => r.RevokeByTokenIdAsync("old-token", "superseded_by_rotation"))
                .ReturnsAsync(true);

            idpSessionService.Setup(s => s.ResolveOrCreateAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync("session-1");

            var service = new UnifiedTokenSessionService(
                cache.Object,
                authDomain.Object,
                refreshRepo.Object,
                activityDispatcher.Object,
                idpSessionService.Object,
                httpContextAccessor.Object,
                userRepo.Object,
                NullLogger<UnifiedTokenSessionService>.Instance);

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
            refreshRepo.Verify(r => r.RevokeByTokenIdAsync("old-token", "superseded_by_rotation", It.IsAny<string>()), Times.Once);
            refreshRepo.Verify(r => r.DeleteAsync(It.IsAny<string>()), Times.Never);
            cache.Verify(c => c.RemoveKeyAsync("old-token"), Times.Once);
        }

        [Fact]
        public async Task CreateOrRotateRefreshToken_FirstIssue_PopulatesLogInCount()
        {
            var cache = new Mock<ICacheClient>();
            var authDomain = new Mock<IAuthenticationDomainService>();
            var refreshRepo = new Mock<IRefreshTokenRepository>();
            var activityDispatcher = new Mock<IUserActivityDispatcher>();
            var idpSessionService = new Mock<IIdpSessionService>();
            var userRepo = new Mock<IUserRepository>();
            var httpContextAccessor = new Mock<IHttpContextAccessor>();

            refreshRepo.Setup(r => r.CreateAsync(It.IsAny<Idp.DomainService.Oidc.Contracts.RefreshTokenModel>()))
                .ReturnsAsync("new-token");

            idpSessionService.Setup(s => s.ResolveOrCreateAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync("session-1");

            var existing = new User
            {
                ItemId = "user-1",
                TokenVersion = 1,
                UserName = "user-1",
                Email = "user-1@test.local",
                LogInCount = 0
            };
            userRepo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(existing);
            userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

            var service = new UnifiedTokenSessionService(
                cache.Object,
                authDomain.Object,
                refreshRepo.Object,
                activityDispatcher.Object,
                idpSessionService.Object,
                httpContextAccessor.Object,
                userRepo.Object,
                NullLogger<UnifiedTokenSessionService>.Instance);

            var tenant = new Tenant
            {
                TenantId = "tenant-1",
                DbConnectionString = string.Empty,
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow },
                Applications = new List<Applications>()
            };

            var tokenRequest = new TokenRequest { ClientId = "client-1", GrantType = "password", Scope = "openid" };
            var config = new IdentityConfiguration
            {
                ItemId = MongoDB.Bson.ObjectId.GenerateNewId(),
                RefreshTokenValidForNumberMinutes = 30,
                AbsoluteRefreshTokenValidForNumberMinutes = 10080
            };

            authDomain.Setup(a => a.GetDeviceInfo(It.IsAny<string>())).Returns(new Iam.DomainService.Dtos.DeviceInformation { Device = "Test" });

            await service.CreateOrRotateRefreshToken(
                null,
                null,
                tokenRequest,
                config,
                tenant,
                existing,
                new[] { "127.0.0.1" },
                impersoanted: false);

            userRepo.Verify(r => r.UpdateUserAsync(It.Is<User>(u => u.LogInCount == 1)), Times.Once);
        }

        [Fact]
        public async Task CreateOrRotateRefreshToken_Rotation_DoesNotIncrementLogInCount()
        {
            var cache = new Mock<ICacheClient>();
            var authDomain = new Mock<IAuthenticationDomainService>();
            var refreshRepo = new Mock<IRefreshTokenRepository>();
            var activityDispatcher = new Mock<IUserActivityDispatcher>();
            var idpSessionService = new Mock<IIdpSessionService>();
            var userRepo = new Mock<IUserRepository>();
            var httpContextAccessor = new Mock<IHttpContextAccessor>();

            refreshRepo.Setup(r => r.CreateAsync(It.IsAny<Idp.DomainService.Oidc.Contracts.RefreshTokenModel>()))
                .ReturnsAsync("new-token");
            refreshRepo.Setup(r => r.RevokeByTokenIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            idpSessionService.Setup(s => s.ResolveOrCreateAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync("session-1");

            var user = new User
            {
                ItemId = "user-1",
                TokenVersion = 1,
                UserName = "user-1",
                Email = "user-1@test.local",
                LogInCount = 4
            };

            var service = new UnifiedTokenSessionService(
                cache.Object,
                authDomain.Object,
                refreshRepo.Object,
                activityDispatcher.Object,
                idpSessionService.Object,
                httpContextAccessor.Object,
                userRepo.Object,
                NullLogger<UnifiedTokenSessionService>.Instance);

            var tenant = new Tenant
            {
                TenantId = "tenant-1",
                DbConnectionString = string.Empty,
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow },
                Applications = new List<Applications>()
            };

            var oldCache = new RefreshTokenCache
            {
                RefreshToken = "old-token",
                SessionId = "session-1",
                TenantId = "tenant-1",
                ClientId = "client-1",
                UserId = "user-1",
                IssuedUtc = DateTime.UtcNow.AddMinutes(-30),
                ExpiresUtc = DateTime.UtcNow,
                AbsoluteExpiresUtc = DateTime.UtcNow.AddDays(7)
            };

            var tokenRequest = new TokenRequest { ClientId = "client-1", GrantType = "refresh_token", Scope = "openid" };
            var config = new IdentityConfiguration
            {
                ItemId = MongoDB.Bson.ObjectId.GenerateNewId(),
                RefreshTokenValidForNumberMinutes = 30,
                AbsoluteRefreshTokenValidForNumberMinutes = 10080
            };

            authDomain.Setup(a => a.GetDeviceInfo(It.IsAny<string>())).Returns(new Iam.DomainService.Dtos.DeviceInformation { Device = "Test" });

            await service.CreateOrRotateRefreshToken(
                "old-token",
                oldCache,
                tokenRequest,
                config,
                tenant,
                user,
                new[] { "127.0.0.1" },
                impersoanted: false);

            userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
            userRepo.Verify(r => r.GetUserByIdAsync(It.IsAny<string>()), Times.Never);
        }
    }
}
