using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Worker;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Worker
{
    public class LogoutAllWorkerServiceTests
    {
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IRefreshTokenRepository> _refreshRepo = new();
        private readonly Mock<ITokenRevocationService> _revocation = new();
        private readonly Mock<IUserActivityDispatcher> _dispatcher = new();

        private LogoutAllWorkerService Create() =>
            new(_cache.Object, _refreshRepo.Object, _revocation.Object, _dispatcher.Object,
                NullLogger<LogoutAllWorkerService>.Instance);

        [Fact]
        public async Task Consume_RevokesActiveTokens_ClearsCache_RevokesAll_AndAudits()
        {
            _refreshRepo.Setup(r => r.GetActiveTokensByUserAsync("user-1"))
                .ReturnsAsync(new List<RefreshTokenModel>
                {
                    new() { TokenId = "t1", UserId = "user-1" },
                    new() { TokenId = "t2", UserId = "user-1" },
                    new() { TokenId = "  ", UserId = "user-1" } // whitespace -> filtered out
                });
            // t1 succeeds; t2 fails -> exercises both the success and warning branches.
            _revocation.Setup(s => s.RevokeTokenAsync("t1", It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new TokenRevocationResult { Success = true });
            _revocation.Setup(s => s.RevokeTokenAsync("t2", It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new TokenRevocationResult { Success = false, Error = "already_revoked" });
            _cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);
            _refreshRepo.Setup(r => r.RevokeAllByTokenIdsAsync(It.IsAny<IEnumerable<string>>(), "logout_all")).ReturnsAsync(2);
            _dispatcher.Setup(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);

            await Create().Consume(new LogoutAllEvent { UserId = "user-1" });

            _revocation.Verify(s => s.RevokeTokenAsync("t1", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _revocation.Verify(s => s.RevokeTokenAsync("t2", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _cache.Verify(c => c.RemoveKeyAsync("t1"), Times.Once);
            _cache.Verify(c => c.RemoveKeyAsync("t2"), Times.Once);
            _refreshRepo.Verify(r => r.RevokeAllByTokenIdsAsync(
                It.Is<IEnumerable<string>>(ids => ids.Contains("t1") && ids.Contains("t2") && ids.Count() == 2),
                "logout_all"), Times.Once);
            _dispatcher.Verify(d => d.SendUserActivityAsync(
                It.Is<UserActivityEvent>(e => e.UserId == "user-1" && e.Event == "LOGGED_OUT_ALL" && e.Category == UserActivityCategory.Auth)),
                Times.Once);
        }

        [Fact]
        public async Task Consume_NoActiveTokens_StillRevokesAll_AndAudits()
        {
            _refreshRepo.Setup(r => r.GetActiveTokensByUserAsync("user-2"))
                .ReturnsAsync(new List<RefreshTokenModel>());
            _refreshRepo.Setup(r => r.RevokeAllByTokenIdsAsync(It.IsAny<IEnumerable<string>>(), "logout_all")).ReturnsAsync(0);
            _dispatcher.Setup(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);

            await Create().Consume(new LogoutAllEvent { UserId = "user-2" });

            _revocation.Verify(s => s.RevokeTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _refreshRepo.Verify(r => r.RevokeAllByTokenIdsAsync(It.Is<IEnumerable<string>>(ids => !ids.Any()), "logout_all"), Times.Once);
            _dispatcher.Verify(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Once);
        }
    }

    public class UserActivityWorkerTests : IDisposable
    {
        private readonly Mock<IUserActivityRepository> _repo = new();

        public UserActivityWorkerTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "org-1",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private UserActivityWorker Create() =>
            new(_repo.Object, NullLogger<UserActivityWorker>.Instance);

        [Fact]
        public async Task Consume_PersistsActivity_FillingContextDefaults()
        {
            _repo.Setup(r => r.InsertAsync(It.IsAny<UserActivity>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            await Create().Consume(new UserActivityEvent
            {
                UserId = "u1",
                Category = UserActivityCategory.Auth,
                Event = "LOGIN",
                Source = "auth"
            });

            _repo.Verify(r => r.InsertAsync(
                It.Is<UserActivity>(d =>
                    d.UserId == "u1" &&
                    d.Event == "LOGIN" &&
                    d.ActorUserId == "actor-1" &&          // falls back to context user
                    d.OrganizationId == "org-1" &&         // from context
                    d.TenantId == "tenant-1"),             // falls back to context tenant
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Consume_PrefersEventProvidedActorAndTenant()
        {
            _repo.Setup(r => r.InsertAsync(It.IsAny<UserActivity>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            await Create().Consume(new UserActivityEvent
            {
                UserId = "u1",
                ActorUserId = "admin-9",
                TenantId = "tenant-explicit",
                Category = UserActivityCategory.Account,
                Event = "USER_UPDATED",
                Source = "iam"
            });

            _repo.Verify(r => r.InsertAsync(
                It.Is<UserActivity>(d => d.ActorUserId == "admin-9" && d.TenantId == "tenant-explicit"),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
