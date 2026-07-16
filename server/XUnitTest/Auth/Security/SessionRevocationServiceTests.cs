using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Security.Models;
using Authentication.DomainService.Security.Repositories;
using Authentication.DomainService.Security.Services;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Security
{
    public class SessionRevocationServiceTests : IDisposable
    {
        private const string Actor = "user-1";
        private readonly Mock<ISecurityRepository> _security = new();
        private readonly Mock<IRefreshTokenRepository> _refresh = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();

        public SessionRevocationServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: Actor, impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "org-1",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
            _activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private SessionRevocationService Create() =>
            new(NullLogger<SessionRevocationService>.Instance, _security.Object, _refresh.Object,
                _cache.Object, _activity.Object);

        private static UserSessionDto ActiveSession(string sessionId = "sess-1") => new()
        {
            SessionId = sessionId,
            Status = SessionStatus.Active,
            ClientIds = new List<string> { "client-1" },
            PrimaryIpAddress = "10.0.0.1"
        };

        // ---- RevokeSessionAsync ----

        [Fact]
        public async Task RevokeSession_EmptySessionId_ReturnsDefaultReason_NoRevoke()
        {
            var result = await Create().RevokeSessionAsync("", Actor, "current", null, CancellationToken.None);
            result.Reason.Should().Be("user_revoked");
            _refresh.Verify(r => r.RevokeAllBySessionIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RevokeSession_SessionNotFound_ReturnsWithoutRevoking()
        {
            _security.Setup(s => s.GetUserSessionAsync(Actor, "sess-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync((UserSessionDto?)null);

            var result = await Create().RevokeSessionAsync("sess-1", Actor, "current", null, CancellationToken.None);

            result.AlreadyRevoked.Should().BeFalse();
            result.Warnings.Should().BeEmpty();
            _refresh.Verify(r => r.RevokeAllBySessionIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RevokeSession_CurrentSession_AddsWarning()
        {
            _security.Setup(s => s.GetUserSessionAsync(Actor, "sess-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(ActiveSession());

            var result = await Create().RevokeSessionAsync("sess-1", Actor, "sess-1", null, CancellationToken.None);

            result.Warnings.Should().Contain("cannot_revoke_current_session");
            _refresh.Verify(r => r.RevokeAllBySessionIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RevokeSession_AlreadyInactive_ReturnsAlreadyRevoked()
        {
            var session = ActiveSession();
            session.Status = SessionStatus.Revoked;
            _security.Setup(s => s.GetUserSessionAsync(Actor, "sess-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(session);

            var result = await Create().RevokeSessionAsync("sess-1", Actor, "current", null, CancellationToken.None);

            result.AlreadyRevoked.Should().BeTrue();
            result.RevokedAt.Should().NotBeNull();
        }

        [Fact]
        public async Task RevokeSession_HappyPath_RevokesTokens_RemovesCache_DispatchesActivity()
        {
            _security.Setup(s => s.GetUserSessionAsync(Actor, "sess-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(ActiveSession());
            _refresh.Setup(r => r.RevokeAllBySessionIdAsync("sess-1", "user_revoked")).ReturnsAsync(3);
            _refresh.Setup(r => r.GetActiveTokensBySessionIdAsync("sess-1"))
                .ReturnsAsync(new List<RefreshTokenModel> { new() { TokenId = "rt-1" }, new() { TokenId = "rt-2" } });
            _cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);

            var result = await Create().RevokeSessionAsync("sess-1", Actor, "current", null, CancellationToken.None);

            result.AlreadyRevoked.Should().BeFalse();
            result.RevokedRefreshTokens.Should().Be(3);
            result.ClientId.Should().Be("client-1");
            result.RevokedAt.Should().NotBeNull();
            _cache.Verify(c => c.RemoveKeyAsync("rt-1"), Times.Once);
            _cache.Verify(c => c.RemoveKeyAsync("rt-2"), Times.Once);
            _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(e => e.SessionId == "sess-1")), Times.Once);
        }

        [Fact]
        public async Task RevokeSession_CustomReason_IsPropagated()
        {
            _security.Setup(s => s.GetUserSessionAsync(Actor, "sess-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(ActiveSession());
            _refresh.Setup(r => r.RevokeAllBySessionIdAsync("sess-1", "suspicious")).ReturnsAsync(1);
            _refresh.Setup(r => r.GetActiveTokensBySessionIdAsync("sess-1")).ReturnsAsync(new List<RefreshTokenModel>());

            var result = await Create().RevokeSessionAsync("sess-1", Actor, "current", "suspicious", CancellationToken.None);

            result.Reason.Should().Be("suspicious");
        }

        [Fact]
        public async Task RevokeSession_RefreshRevokeThrows_AddsWarning_StillSucceeds()
        {
            _security.Setup(s => s.GetUserSessionAsync(Actor, "sess-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(ActiveSession());
            _refresh.Setup(r => r.RevokeAllBySessionIdAsync("sess-1", It.IsAny<string>()))
                .ThrowsAsync(new Exception("db down"));
            _refresh.Setup(r => r.GetActiveTokensBySessionIdAsync("sess-1")).ReturnsAsync(new List<RefreshTokenModel>());

            var result = await Create().RevokeSessionAsync("sess-1", Actor, "current", null, CancellationToken.None);

            result.Warnings.Should().Contain("refresh_token_revoke_skipped:db_unreachable");
            result.RevokedRefreshTokens.Should().Be(0);
        }

        // ---- RevokeRefreshTokenAsync ----

        [Fact]
        public async Task RevokeRefreshToken_EmptyTokenId_ReturnsDefault()
        {
            var result = await Create().RevokeRefreshTokenAsync("", Actor, "current", null, CancellationToken.None);
            result.Reason.Should().Be("user_revoked");
            _refresh.Verify(r => r.RevokeByTokenIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RevokeRefreshToken_NotFound_AddsWarning()
        {
            _refresh.Setup(r => r.GetByTokenIdAsync("rt-1")).ReturnsAsync((RefreshTokenModel)null!);

            var result = await Create().RevokeRefreshTokenAsync("rt-1", Actor, "current", null, CancellationToken.None);

            result.Warnings.Should().Contain("refresh_token_not_found");
        }

        [Fact]
        public async Task RevokeRefreshToken_CurrentSession_AddsWarning()
        {
            _refresh.Setup(r => r.GetByTokenIdAsync("rt-1"))
                .ReturnsAsync(new RefreshTokenModel { TokenId = "rt-1", SessionId = "sess-current", ClientId = "c1" });

            var result = await Create().RevokeRefreshTokenAsync("rt-1", Actor, "sess-current", null, CancellationToken.None);

            result.Warnings.Should().Contain("cannot_revoke_current_session");
            result.SessionId.Should().Be("sess-current");
            _refresh.Verify(r => r.RevokeByTokenIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RevokeRefreshToken_HappyPath_RevokesAndRemovesCache()
        {
            _refresh.Setup(r => r.GetByTokenIdAsync("rt-1"))
                .ReturnsAsync(new RefreshTokenModel { TokenId = "rt-1", SessionId = "sess-2", ClientId = "c1", IpAddress = "10.0.0.9" });
            _refresh.Setup(r => r.RevokeByTokenIdAsync("rt-1", "user_revoked")).ReturnsAsync(true);
            _cache.Setup(c => c.RemoveKeyAsync("rt-1")).ReturnsAsync(true);

            var result = await Create().RevokeRefreshTokenAsync("rt-1", Actor, "current", null, CancellationToken.None);

            result.RevokedRefreshTokens.Should().Be(1);
            result.RevokedAt.Should().NotBeNull();
            result.SessionId.Should().Be("sess-2");
            result.ClientId.Should().Be("c1");
            _cache.Verify(c => c.RemoveKeyAsync("rt-1"), Times.Once);
            _activity.Verify(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Once);
        }
    }
}
