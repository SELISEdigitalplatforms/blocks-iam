using Api.Controllers;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Security.Models;
using Authentication.DomainService.Security.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Activity.RequestModel;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Security
{
    public class SecurityControllerTests : IDisposable
    {
        private const string ActorUserId = "user-1";
        private const string SessionId = "session-1";

        private readonly Mock<ISecurityQueryService> _securityQuery;
        private readonly Mock<ISessionRevocationService> _revocation;
        private readonly Mock<IActivityQueryService> _activity;
        private readonly Mock<IRefreshTokenRepository> _refreshTokens;

        public SecurityControllerTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1",
                roles: null,
                userId: ActorUserId,
                impersonated: false,
                isAuthenticated: true,
                requestUri: "https://test/security",
                organizationId: "default",
                permissions: null,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "user@blocks.com",
                userName: "tester",
                phoneNumber: null,
                displayName: "Tester",
                oauthToken: null,
                originalTenantId: "tenant-1",
                impersonationSessionId: null,
                applicationDomain: "test"));

            _securityQuery = new Mock<ISecurityQueryService>(MockBehavior.Strict);
            _revocation = new Mock<ISessionRevocationService>(MockBehavior.Strict);
            _activity = new Mock<IActivityQueryService>(MockBehavior.Strict);
            _refreshTokens = new Mock<IRefreshTokenRepository>(MockBehavior.Strict);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private SecurityController CreateController()
        {
            return new SecurityController(
                _securityQuery.Object,
                _revocation.Object,
                _activity.Object);
        }

        [Fact]
        public async Task GetSummary_ReturnsOkWithDto()
        {
            var summary = new SecuritySummaryDto
            {
                CurrentSessionId = SessionId,
                TotalSessions = 2,
                ActiveSessions = 1,
                ExpiredSessions = 1,
                RevokedSessions = 0,
                LastActivityAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow.AddHours(-1),
            };
            _securityQuery.Setup(s => s.GetSecuritySummaryAsync(ActorUserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(summary);

            var result = await CreateController().GetSummary(CancellationToken.None);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().BeSameAs(summary);
        }

        [Fact]
        public async Task GetSessions_ReturnsOkWithList()
        {
            var sessions = new List<UserSessionDto>
            {
                new()
                {
                    SessionId = SessionId,
                    IsCurrent = true,
                    Status = SessionStatus.Active,
                    LastActivityAt = DateTime.UtcNow,
                }
            };
            _securityQuery.Setup(s => s.GetUserSessionsAsync(ActorUserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(sessions);

            var result = await CreateController().GetUserSessions(CancellationToken.None);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().BeSameAs(sessions);
        }

        [Fact]
        public async Task GetSessionDetails_NotFound_ReturnsSession_Not_Found()
        {
            _securityQuery.Setup(s => s.GetSessionDetailsAsync(ActorUserId, "missing", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SessionDetailsDto { Overview = null });

            var result = await CreateController().GetSessionDetails("missing", CancellationToken.None);

            var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
            notFound.Value.Should().NotBeNull();
        }

        [Fact]
        public async Task GetSessionDetails_Found_ReturnsOkWithDetails()
        {
            var details = new SessionDetailsDto
            {
                Overview = new SessionOverviewDto { SessionId = SessionId },
                Applications = new List<ApplicationDto> { new() { ClientId = "c1" } },
                Timeline = new List<TimelineEventDto> { new() { At = DateTime.UtcNow } },
            };
            _securityQuery.Setup(s => s.GetSessionDetailsAsync(ActorUserId, SessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(details);

            var result = await CreateController().GetSessionDetails(SessionId, CancellationToken.None);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task RevokeSession_CurrentSession_ReturnsBadRequest()
        {
            _securityQuery.Setup(s => s.ResolveCurrentSessionIdAsync(ActorUserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(SessionId);

            var response = new RevokeSessionResponse
            {
                SessionId = SessionId,
                Warnings = new List<string> { "cannot_revoke_current_session" },
            };
            _revocation.Setup(r => r.RevokeSessionAsync(SessionId, ActorUserId, SessionId, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            var result = await CreateController().RevokeSession(SessionId, null, CancellationToken.None);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().NotBeNull();
        }

        [Fact]
        public async Task RevokeSession_OtherSession_ReturnsOk()
        {
            _securityQuery.Setup(s => s.ResolveCurrentSessionIdAsync(ActorUserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync("current-session");

            var response = new RevokeSessionResponse
            {
                SessionId = "other-session",
                AlreadyRevoked = false,
                RevokedAt = DateTime.UtcNow,
                RevokedRefreshTokens = 1,
                Warnings = new List<string>(),
            };
            _revocation.Setup(r => r.RevokeSessionAsync(
                    "other-session",
                    ActorUserId,
                    "current-session",
                    "user_revoked",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            var result = await CreateController().RevokeSession("other-session", new() { Reason = "user_revoked" }, CancellationToken.None);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetActivity_DelegatesToActivityQueryService()
        {
            var response = new ActivityPageResponse
            {
                Items = new List<ActivityItemDto>
                {
                    new()
                    {
                        ItemId = "1",
                        UserId = ActorUserId,
                        ActorUserId = ActorUserId,
                        Category = UserActivityCategory.Auth,
                        Event = "LOGIN_SUCCESS",
                        CreatedDate = DateTime.UtcNow,
                    }
                },
                TotalCount = 1,
                Page = 0,
                PageSize = 25,
            };
            _activity.Setup(a => a.GetActivityPageAsync(
                    ActorUserId,
                    It.IsAny<GetActivitiesRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            var result = await CreateController().GetActivity(
                page: null,
                pageSize: null,
                sessionId: "session-1",
                clientId: null,
                eventName: "LOGIN_SUCCESS",
                eventsCsv: "LOGIN_FAILURE,SESSION_REVOKED",
                outcomesCsv: "Success",
                categoriesCsv: "Auth",
                from: null,
                to: null,
                search: "ip",
                ct: CancellationToken.None);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().BeSameAs(response);
            _activity.Verify(a => a.GetActivityPageAsync(
                ActorUserId,
                It.Is<GetActivitiesRequest>(r =>
                    r.Page == 0
                    && r.PageSize == 25
                    && r.Filter!.SessionId == "session-1"
                    && r.Filter.Events!.Contains("LOGIN_SUCCESS")
                    && r.Filter.Events!.Contains("LOGIN_FAILURE")
                    && r.Filter.Events!.Contains("SESSION_REVOKED")
                    && r.Filter.Outcomes!.Contains("Success")
                    && r.Filter.Categories!.Contains(UserActivityCategory.Auth)
                    && r.Filter.Search == "ip"),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
