using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Security.Models;
using Authentication.DomainService.Security.Repositories;
using Authentication.DomainService.Security.Services;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Iam.DomainService.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Security
{
    public class SecurityQueryServiceTests
    {
        private readonly Mock<ISecurityRepository> _securityRepo = new();
        private readonly Mock<IActivityQueryService> _activity = new();
        private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
        private readonly Mock<IHttpContextAccessor> _httpAccessor = new();

        private SecurityQueryService Create() =>
            new(NullLogger<SecurityQueryService>.Instance,
                _securityRepo.Object,
                _activity.Object,
                _refreshTokens.Object,
                _httpAccessor.Object);

        private void UseNoHttpContext() => _httpAccessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);

        private DefaultHttpContext UseHttpContext(string host = "example.com")
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Host = new HostString(host);
            _httpAccessor.Setup(a => a.HttpContext).Returns(ctx);
            return ctx;
        }

        // ---------- GetSecuritySummaryAsync ----------

        [Fact]
        public async Task GetSecuritySummary_ReturnsEmpty_WhenUserIdBlank()
        {
            var result = await Create().GetSecuritySummaryAsync("  ", CancellationToken.None);

            result.TotalSessions.Should().Be(0);
            result.ActiveSessions.Should().Be(0);
            result.CurrentSessionId.Should().BeNull();
            _securityRepo.Verify(r => r.GetUserSessionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetSecuritySummary_AggregatesSessionCountsAndTimestamps()
        {
            UseNoHttpContext();
            var sessions = new List<UserSessionDto>
            {
                new() { SessionId = "s1", Status = SessionStatus.Active, CreatedAt = new DateTime(2026, 1, 1), LastActivityAt = new DateTime(2026, 6, 1) },
                new() { SessionId = "s2", Status = SessionStatus.Expired, CreatedAt = new DateTime(2026, 2, 1), LastActivityAt = new DateTime(2026, 5, 1) },
                new() { SessionId = "s3", Status = SessionStatus.Revoked, CreatedAt = new DateTime(2026, 3, 1), LastActivityAt = new DateTime(2026, 7, 1) },
            };
            _securityRepo.Setup(r => r.GetUserSessionsAsync("u1", null, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(sessions);

            var result = await Create().GetSecuritySummaryAsync("u1", CancellationToken.None);

            result.TotalSessions.Should().Be(3);
            result.ActiveSessions.Should().Be(1);
            result.ExpiredSessions.Should().Be(1);
            result.RevokedSessions.Should().Be(1);
            result.LastActivityAt.Should().Be(new DateTime(2026, 7, 1));
            result.LastLoginAt.Should().Be(new DateTime(2026, 3, 1));
            result.CurrentSessionId.Should().BeNull();
        }

        [Fact]
        public async Task GetSecuritySummary_SetsCurrentSessionId_FromRefreshTokenCookie()
        {
            var ctx = UseHttpContext();
            ctx.Request.Headers["Cookie"] = "tetorefreshtoken_example.com=tok-1";
            _refreshTokens.Setup(r => r.GetByTokenIdAsync("tok-1"))
                .ReturnsAsync(new RefreshTokenModel { SessionId = "s2" });

            var sessions = new List<UserSessionDto>
            {
                new() { SessionId = "s1", Status = SessionStatus.Active },
                new() { SessionId = "s2", Status = SessionStatus.Active },
            };
            _securityRepo.Setup(r => r.GetUserSessionsAsync("u1", null, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(sessions);

            var result = await Create().GetSecuritySummaryAsync("u1", CancellationToken.None);

            result.CurrentSessionId.Should().Be("s2");
            result.TotalSessions.Should().Be(2);
        }

        [Fact]
        public async Task GetSecuritySummary_EmptySessions_YieldsZeroCountsAndMinValueTimestamps()
        {
            UseNoHttpContext();
            _securityRepo.Setup(r => r.GetUserSessionsAsync("u1", null, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<UserSessionDto>());

            var result = await Create().GetSecuritySummaryAsync("u1", CancellationToken.None);

            result.TotalSessions.Should().Be(0);
            result.ActiveSessions.Should().Be(0);
            // DefaultIfEmpty().Max() over an empty sequence collapses to DateTime.MinValue.
            result.LastActivityAt.Should().Be(DateTime.MinValue);
            result.LastLoginAt.Should().Be(DateTime.MinValue);
        }

        // ---------- GetUserSessionsAsync ----------

        [Fact]
        public async Task GetUserSessions_ReturnsEmpty_WhenUserIdBlank()
        {
            var result = await Create().GetUserSessionsAsync("", CancellationToken.None);

            result.Should().BeEmpty();
            _securityRepo.Verify(r => r.GetUserSessionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetUserSessions_MarksCurrentSession()
        {
            var ctx = UseHttpContext();
            ctx.Request.Headers["Cookie"] = "tetorefreshtoken_example.com=tok-1";
            _refreshTokens.Setup(r => r.GetByTokenIdAsync("tok-1"))
                .ReturnsAsync(new RefreshTokenModel { SessionId = "s2" });

            var sessions = new List<UserSessionDto>
            {
                new() { SessionId = "s1" },
                new() { SessionId = "s2" },
                new() { SessionId = "s3" },
            };
            _securityRepo.Setup(r => r.GetUserSessionsAsync("u1", null, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(sessions);

            var result = await Create().GetUserSessionsAsync("u1", CancellationToken.None);

            result.Should().HaveCount(3);
            result.Single(s => s.SessionId == "s2").IsCurrent.Should().BeTrue();
            result.Where(s => s.SessionId != "s2").Should().OnlyContain(s => s.IsCurrent == false);
        }

        [Fact]
        public async Task GetUserSessions_NoCurrentSession_NoneMarkedCurrent()
        {
            UseNoHttpContext();
            var sessions = new List<UserSessionDto> { new() { SessionId = "s1" }, new() { SessionId = "s2" } };
            _securityRepo.Setup(r => r.GetUserSessionsAsync("u1", null, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(sessions);

            var result = await Create().GetUserSessionsAsync("u1", CancellationToken.None);

            result.Should().OnlyContain(s => s.IsCurrent == false);
        }

        // ---------- GetSessionDetailsAsync ----------

        [Fact]
        public async Task GetSessionDetails_ReturnsEmpty_WhenIdsBlank()
        {
            var result = await Create().GetSessionDetailsAsync("", "", CancellationToken.None);

            result.Overview.Should().BeNull();
            result.Applications.Should().BeEmpty();
            result.Timeline.Should().BeEmpty();
        }

        [Fact]
        public async Task GetSessionDetails_ReturnsEmpty_WhenSessionMissing()
        {
            _securityRepo.Setup(r => r.GetUserSessionAsync("u1", "s1", It.IsAny<CancellationToken>()))
                .ReturnsAsync((UserSessionDto?)null);

            var result = await Create().GetSessionDetailsAsync("u1", "s1", CancellationToken.None);

            result.Overview.Should().BeNull();
        }

        [Fact]
        public async Task GetSessionDetails_BuildsOverviewApplicationsAndTimeline()
        {
            UseNoHttpContext();
            var session = new UserSessionDto
            {
                SessionId = "s1",
                Status = SessionStatus.Active,
                ClientIds = new List<string> { "app1", "app1", "app2", "" },
                PrimaryDeviceName = "MacBook",
                PrimaryOperatingSystem = "macOS",
                PrimaryBrowser = "Chrome",
                PrimaryIpAddress = "1.2.3.4",
                CreatedAt = new DateTime(2026, 5, 1),
                LastActivityAt = new DateTime(2026, 6, 10),
                AbsoluteExpiry = new DateTime(2026, 7, 1),
                IdleExpiry = new DateTime(2026, 6, 15),
            };
            _securityRepo.Setup(r => r.GetUserSessionAsync("u1", "s1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(session);

            var activities = new List<ActivityItemDto>
            {
                new()
                {
                    CreatedDate = new DateTime(2026, 6, 1),
                    Event = "login",
                    Outcome = "success",
                    ClientId = "app1",
                    Context = new ActivityContext { IpAddress = "1.2.3.4", DeviceName = "MacBook" },
                },
            };
            _activity.Setup(a => a.GetActivitiesForSessionAsync("u1", "s1", 0, 50, It.IsAny<CancellationToken>()))
                .ReturnsAsync(activities);

            var rotations = new List<SessionRotationRecord>
            {
                new()
                {
                    ClientId = "app1",
                    IssuedUtc = new DateTime(2026, 6, 2),
                    IsRevoked = true,
                    RevokedAt = new DateTime(2026, 6, 3),
                    RevokeReason = "logout",
                },
            };
            _securityRepo.Setup(r => r.GetRotationHistoryAsync("s1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(rotations);

            var result = await Create().GetSessionDetailsAsync("u1", "s1", CancellationToken.None);

            result.Overview.Should().NotBeNull();
            result.Overview!.SessionId.Should().Be("s1");
            result.Overview.DeviceName.Should().Be("MacBook");
            result.Overview.OperatingSystem.Should().Be("macOS");
            result.Overview.Browser.Should().Be("Chrome");
            result.Overview.IpAddress.Should().Be("1.2.3.4");
            result.Overview.IsCurrent.Should().BeFalse();

            // ClientIds distinct + non-empty
            result.Applications.Select(a => a.ClientId).Should().BeEquivalentTo("app1", "app2");

            // Auth(6/1) -> Refresh(6/2) -> Revocation(6/3), ordered by At
            result.Timeline.Should().HaveCount(3);
            result.Timeline.Select(t => t.Type).Should()
                .ContainInOrder(TimelineEventType.Auth, TimelineEventType.Refresh, TimelineEventType.Revocation);
            result.Timeline[0].Event.Should().Be("login");
            result.Timeline[0].IpAddress.Should().Be("1.2.3.4");
            result.Timeline[2].ReasonCode.Should().Be("logout");
        }

        [Fact]
        public async Task GetSessionDetails_RotationNotRevoked_OmitsRevocationEvent()
        {
            UseNoHttpContext();
            var session = new UserSessionDto
            {
                SessionId = "s1",
                Status = SessionStatus.Active,
                ClientIds = new List<string> { "app1" },
            };
            _securityRepo.Setup(r => r.GetUserSessionAsync("u1", "s1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(session);
            _activity.Setup(a => a.GetActivitiesForSessionAsync("u1", "s1", 0, 50, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ActivityItemDto>());
            _securityRepo.Setup(r => r.GetRotationHistoryAsync("s1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SessionRotationRecord>
                {
                    new() { ClientId = "app1", IssuedUtc = new DateTime(2026, 6, 2), IsRevoked = false },
                });

            var result = await Create().GetSessionDetailsAsync("u1", "s1", CancellationToken.None);

            result.Timeline.Should().ContainSingle();
            result.Timeline[0].Type.Should().Be(TimelineEventType.Refresh);
        }

        // ---------- ResolveCurrentSessionIdAsync ----------

        [Fact]
        public async Task ResolveCurrentSessionId_ReturnsNull_WhenNoHttpContext()
        {
            UseNoHttpContext();

            var result = await Create().ResolveCurrentSessionIdAsync("u1", CancellationToken.None);

            result.Should().BeNull();
        }

        [Fact]
        public async Task ResolveCurrentSessionId_ReturnsNull_WhenUserIdBlank()
        {
            UseHttpContext();

            var result = await Create().ResolveCurrentSessionIdAsync("  ", CancellationToken.None);

            result.Should().BeNull();
        }

        [Fact]
        public async Task ResolveCurrentSessionId_ReturnsNull_WhenNoToken()
        {
            UseHttpContext();

            var result = await Create().ResolveCurrentSessionIdAsync("u1", CancellationToken.None);

            result.Should().BeNull();
        }

        [Fact]
        public async Task ResolveCurrentSessionId_UsesCookieToken()
        {
            var ctx = UseHttpContext();
            ctx.Request.Headers["Cookie"] = "tetorefreshtoken_example.com=cookie-tok";
            _refreshTokens.Setup(r => r.GetByTokenIdAsync("cookie-tok"))
                .ReturnsAsync(new RefreshTokenModel { SessionId = "sess-cookie" });

            var result = await Create().ResolveCurrentSessionIdAsync("u1", CancellationToken.None);

            result.Should().Be("sess-cookie");
        }

        [Fact]
        public async Task ResolveCurrentSessionId_FallsBackToHeaderToken()
        {
            var ctx = UseHttpContext();
            // Header named exactly as the cookie key (not a Cookie header) exercises the fallback.
            ctx.Request.Headers["tetorefreshtoken_example.com"] = "header-tok";
            _refreshTokens.Setup(r => r.GetByTokenIdAsync("header-tok"))
                .ReturnsAsync(new RefreshTokenModel { SessionId = "sess-header" });

            var result = await Create().ResolveCurrentSessionIdAsync("u1", CancellationToken.None);

            result.Should().Be("sess-header");
        }

        [Fact]
        public async Task ResolveCurrentSessionId_ReturnsNull_WhenRepositoryThrows()
        {
            var ctx = UseHttpContext();
            ctx.Request.Headers["Cookie"] = "tetorefreshtoken_example.com=boom";
            _refreshTokens.Setup(r => r.GetByTokenIdAsync("boom"))
                .ThrowsAsync(new InvalidOperationException("db down"));

            var result = await Create().ResolveCurrentSessionIdAsync("u1", CancellationToken.None);

            result.Should().BeNull();
        }

        [Fact]
        public async Task ResolveCurrentSessionId_ReturnsNull_WhenTokenNotFound()
        {
            var ctx = UseHttpContext();
            ctx.Request.Headers["Cookie"] = "tetorefreshtoken_example.com=unknown";
            _refreshTokens.Setup(r => r.GetByTokenIdAsync("unknown"))
                .ReturnsAsync((RefreshTokenModel)null!);

            var result = await Create().ResolveCurrentSessionIdAsync("u1", CancellationToken.None);

            result.Should().BeNull();
        }
    }
}
