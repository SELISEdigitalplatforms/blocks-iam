using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
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
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.Worker
{
    public class LogoutAllWorkerServiceTests
    {
        private readonly Mock<IUserSessionTerminator> _terminator = new();
        private readonly Mock<IUserActivityDispatcher> _dispatcher = new();

        private LogoutAllWorkerService Create() => new(_terminator.Object, _dispatcher.Object);

        [Fact]
        public async Task Consume_EndsAllSessions_ThenAudits()
        {
            _dispatcher.Setup(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);

            await Create().Consume(new LogoutAllEvent { UserId = "user-1" });

            _terminator.Verify(t => t.EndAllSessionsAsync("user-1", "logout_all"), Times.Once);
            _dispatcher.Verify(d => d.SendUserActivityAsync(
                It.Is<UserActivityEvent>(e => e.UserId == "user-1" && e.Event == "LOGGED_OUT_ALL" && e.Category == UserActivityCategory.Auth)),
                Times.Once);
        }

        [Fact]
        public async Task Consume_TerminatorFails_Throws_AndDoesNotAudit()
        {
            _terminator.Setup(t => t.EndAllSessionsAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("db down"));

            var act = () => Create().Consume(new LogoutAllEvent { UserId = "user-1" });

            await act.Should().ThrowAsync<InvalidOperationException>();
            _dispatcher.Verify(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Never);
        }
    }

    public class UserSessionTerminatorTests
    {
        private readonly Mock<ITokenRevocationService> _revocation = new();
        private readonly Mock<IIdpSessionService> _sessions = new();

        private UserSessionTerminator Create() => new(_revocation.Object, _sessions.Object);

        private static void InstallContext(string? tenantId)
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: tenantId, roles: null, userId: "caller", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: tenantId, impersonationSessionId: null, applicationDomain: "test"));
        }

        private static void ClearContext()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        [Fact]
        public async Task EndAll_RevokesTokens_AndRemovesOnlyThisUserFromLiveSessions()
        {
            InstallContext("tenant-1");
            try
            {
                _revocation.Setup(r => r.RevokeAllUserTokensAsync("user-3", "tenant-1", "password_reset")).ReturnsAsync(true);
                _sessions.Setup(s => s.GetUserSessionsAsync("user-3", "tenant-1")).ReturnsAsync(new List<IdpSessionModel>
                {
                    new() { SessionId = "live-1" },
                    new() { SessionId = "gone-1", RevokedAt = DateTime.UtcNow.AddMinutes(-5) }
                });
                _sessions.Setup(s => s.RemoveAccountAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync(true);

                await Create().EndAllSessionsAsync("user-3", "password_reset");

                _revocation.Verify(r => r.RevokeAllUserTokensAsync("user-3", "tenant-1", "password_reset"), Times.Once);
                _sessions.Verify(s => s.RemoveAccountAsync("live-1", "user-3", "tenant-1"), Times.Once);
                _sessions.Verify(s => s.RemoveAccountAsync("gone-1", It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
                _sessions.Verify(s => s.RevokeSessionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            }
            finally
            {
                ClearContext();
            }
        }

        [Fact]
        public async Task EndAll_NoTenant_Throws_AndTouchesNothing()
        {
            InstallContext(null);
            try
            {
                var act = () => Create().EndAllSessionsAsync("user-3", "password_reset");

                await act.Should().ThrowAsync<InvalidOperationException>();
                _revocation.Verify(r => r.RevokeAllUserTokensAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            }
            finally
            {
                ClearContext();
            }
        }

        [Fact]
        public async Task EndAll_TokenRevokeFails_Throws_SoCallerCanQueue()
        {
            InstallContext("tenant-1");
            try
            {
                _revocation.Setup(r => r.RevokeAllUserTokensAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                    .ThrowsAsync(new TimeoutException("mongo"));

                var act = () => Create().EndAllSessionsAsync("user-3", "password_reset");

                await act.Should().ThrowAsync<TimeoutException>();
            }
            finally
            {
                ClearContext();
            }
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
