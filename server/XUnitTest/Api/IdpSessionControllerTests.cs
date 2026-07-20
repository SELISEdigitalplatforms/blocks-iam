using System.Security.Claims;
using Authentication.DomainService.Oidc.Services;
using Blocks.Api.Controllers;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.ApiTests
{
    /// <summary>
    /// Unit tests for <see cref="IdpSessionController"/>. The session service is mocked; the controller
    /// is driven with a <see cref="DefaultHttpContext"/> whose "sid" claim carries the session id and
    /// whose Authorization header satisfies the CSRF (same-origin/Bearer) gate for mutation endpoints.
    /// </summary>
    public class IdpSessionControllerTests : IDisposable
    {
        private const string TenantId = "tenant-1";
        private const string SessionId = "session-1";

        private readonly Mock<IIdpSessionService> _sessionService = new();
        private readonly Mock<ITenants> _tenants = new();

        public IdpSessionControllerTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: TenantId, roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test/oidc/session", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: TenantId, impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private IdpSessionController CreateController(string? sid = SessionId, bool bearer = true)
        {
            var controller = new IdpSessionController(
                _sessionService.Object,
                NullLogger<IdpSessionController>.Instance,
                _tenants.Object);

            var httpCtx = new DefaultHttpContext();
            if (bearer)
            {
                httpCtx.Request.Headers["Authorization"] = "Bearer test-token";
            }

            var claims = new List<Claim>();
            if (sid != null)
            {
                claims.Add(new Claim("sid", sid));
            }
            httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

            controller.ControllerContext = new ControllerContext { HttpContext = httpCtx };
            return controller;
        }

        private static IdpSessionModel ValidSession() => new()
        {
            SessionId = SessionId,
            TenantId = TenantId,
            Accounts = new List<IdpSessionAccount>
            {
                new() { UserId = "u-1", TenantId = TenantId, DisplayName = "User One", LoginAt = DateTime.UtcNow }
            },
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            LastActivityAt = DateTime.UtcNow.AddMinutes(-1),
            IdleExpiry = DateTime.UtcNow.AddHours(1),
            AbsoluteExpiry = DateTime.UtcNow.AddHours(4),
            RevokedAt = null
        };

        // ---------- GetSession ----------

        [Fact]
        public async Task GetSession_NoSessionId_ReturnsUnauthorized()
        {
            var result = await CreateController(sid: null, bearer: false).GetSession();

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task GetSession_SessionNotFound_ReturnsNotFound()
        {
            _sessionService.Setup(s => s.GetSessionAsync(SessionId)).ReturnsAsync((IdpSessionModel)null);

            var result = await CreateController().GetSession();

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Fact]
        public async Task GetSession_Revoked_ReturnsNotFound()
        {
            var session = ValidSession();
            session.RevokedAt = DateTime.UtcNow.AddMinutes(-1);
            _sessionService.Setup(s => s.GetSessionAsync(SessionId)).ReturnsAsync(session);

            var result = await CreateController().GetSession();

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Fact]
        public async Task GetSession_Expired_ReturnsUnauthorized()
        {
            var session = ValidSession();
            session.AbsoluteExpiry = DateTime.UtcNow.AddHours(-1);
            _sessionService.Setup(s => s.GetSessionAsync(SessionId)).ReturnsAsync(session);

            var result = await CreateController().GetSession();

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task GetSession_Valid_ReturnsOkAndTouchesActivity()
        {
            _sessionService.Setup(s => s.GetSessionAsync(SessionId)).ReturnsAsync(ValidSession());

            var result = await CreateController().GetSession();

            result.Should().BeOfType<OkObjectResult>();
            _sessionService.Verify(s => s.UpdateActivityAsync(SessionId), Times.Once);
        }

        // ---------- GetAccounts ----------

        [Fact]
        public async Task GetAccounts_NoSessionId_ReturnsUnauthorized()
        {
            var result = await CreateController(sid: null, bearer: false).GetAccounts();

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task GetAccounts_SessionInactive_ReturnsUnauthorized()
        {
            _sessionService.Setup(s => s.IsSessionActiveAsync(SessionId)).ReturnsAsync(false);

            var result = await CreateController().GetAccounts();

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task GetAccounts_Active_ReturnsOkAndTouchesActivity()
        {
            _sessionService.Setup(s => s.IsSessionActiveAsync(SessionId)).ReturnsAsync(true);
            _sessionService.Setup(s => s.GetAccountsAsync(SessionId)).ReturnsAsync(new List<IdpSessionAccount>
            {
                new() { UserId = "u-1", TenantId = TenantId, DisplayName = null, LoginAt = DateTime.UtcNow }
            });

            var result = await CreateController().GetAccounts();

            result.Should().BeOfType<OkObjectResult>();
            _sessionService.Verify(s => s.UpdateActivityAsync(SessionId), Times.Once);
        }

        // ---------- AddAccount ----------

        [Fact]
        public async Task AddAccount_MissingFields_ReturnsBadRequest()
        {
            var result = await CreateController().AddAccount(new AddAccountRequest { UserId = "", TenantId = "" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task AddAccount_UntrustedOrigin_ReturnsForbidden()
        {
            var result = await CreateController(bearer: false)
                .AddAccount(new AddAccountRequest { UserId = "u-2", TenantId = TenantId });

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        }

        [Fact]
        public async Task AddAccount_NoSessionId_ReturnsUnauthorized()
        {
            var result = await CreateController(sid: null)
                .AddAccount(new AddAccountRequest { UserId = "u-2", TenantId = TenantId });

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task AddAccount_ServiceFails_ReturnsBadRequest()
        {
            _sessionService.Setup(s => s.AddAccountAsync(SessionId, "u-2", TenantId, It.IsAny<string>()))
                .ReturnsAsync(false);

            var result = await CreateController()
                .AddAccount(new AddAccountRequest { UserId = "u-2", TenantId = TenantId, DisplayName = "Two" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task AddAccount_Success_ReturnsOkRotatesAndSetsCookie()
        {
            _sessionService.Setup(s => s.AddAccountAsync(SessionId, "u-2", TenantId, It.IsAny<string>()))
                .ReturnsAsync(true);
            _sessionService.Setup(s => s.RotateSessionAsync(SessionId, "account_add")).ReturnsAsync("rotated-sid");

            var controller = CreateController();
            var result = await controller.AddAccount(new AddAccountRequest { UserId = "u-2", TenantId = TenantId, DisplayName = "Two" });

            result.Should().BeOfType<OkObjectResult>();
            _sessionService.Verify(s => s.UpdateActivityAsync(SessionId), Times.Once);
            _sessionService.Verify(s => s.RotateSessionAsync(SessionId, "account_add"), Times.Once);
            controller.Response.Headers.SetCookie.ToString().Should().Contain("idp_session_id_");
        }

        // ---------- SelectAccount ----------

        [Fact]
        public async Task SelectAccount_MissingUserId_ReturnsBadRequest()
        {
            var result = await CreateController().SelectAccount(new SelectAccountRequest { UserId = "" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task SelectAccount_Untrusted_ReturnsForbidden()
        {
            var result = await CreateController(bearer: false).SelectAccount(new SelectAccountRequest { UserId = "u-2" });

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        }

        [Fact]
        public async Task SelectAccount_NoSessionId_ReturnsUnauthorized()
        {
            var result = await CreateController(sid: null).SelectAccount(new SelectAccountRequest { UserId = "u-2" });

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task SelectAccount_NotInSession_ReturnsBadRequest()
        {
            _sessionService.Setup(s => s.SelectAccountAsync(SessionId, "u-2")).ReturnsAsync(false);

            var result = await CreateController().SelectAccount(new SelectAccountRequest { UserId = "u-2" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task SelectAccount_Success_ReturnsOkAndRotates()
        {
            _sessionService.Setup(s => s.SelectAccountAsync(SessionId, "u-2")).ReturnsAsync(true);
            _sessionService.Setup(s => s.RotateSessionAsync(SessionId, "account_select")).ReturnsAsync("rotated-sid");

            var result = await CreateController().SelectAccount(new SelectAccountRequest { UserId = "u-2" });

            result.Should().BeOfType<OkObjectResult>();
            _sessionService.Verify(s => s.RotateSessionAsync(SessionId, "account_select"), Times.Once);
        }

        // ---------- RemoveAccount ----------

        [Fact]
        public async Task RemoveAccount_MissingUserId_ReturnsBadRequest()
        {
            var result = await CreateController().RemoveAccount("");

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task RemoveAccount_Untrusted_ReturnsForbidden()
        {
            var result = await CreateController(bearer: false).RemoveAccount("u-2");

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        }

        [Fact]
        public async Task RemoveAccount_NoSessionId_ReturnsUnauthorized()
        {
            var result = await CreateController(sid: null).RemoveAccount("u-2");

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task RemoveAccount_ServiceFails_ReturnsBadRequest()
        {
            _sessionService.Setup(s => s.RemoveAccountAsync(SessionId, "u-2", It.IsAny<string>())).ReturnsAsync(false);

            var result = await CreateController().RemoveAccount("u-2");

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task RemoveAccount_Success_ReturnsOkAndRotates()
        {
            _sessionService.Setup(s => s.RemoveAccountAsync(SessionId, "u-2", It.IsAny<string>())).ReturnsAsync(true);
            _sessionService.Setup(s => s.RotateSessionAsync(SessionId, "account_remove")).ReturnsAsync("rotated-sid");

            var result = await CreateController().RemoveAccount("u-2");

            result.Should().BeOfType<OkObjectResult>();
            _sessionService.Verify(s => s.RotateSessionAsync(SessionId, "account_remove"), Times.Once);
        }

        // ---------- RevokeSession ----------

        [Fact]
        public async Task RevokeSession_Untrusted_ReturnsForbidden()
        {
            var result = await CreateController(bearer: false).RevokeSession(new RevokeSessionRequest { Reason = "x" });

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        }

        [Fact]
        public async Task RevokeSession_NoSessionId_ReturnsUnauthorized()
        {
            var result = await CreateController(sid: null).RevokeSession(null);

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task RevokeSession_ServiceFails_ReturnsBadRequest()
        {
            _sessionService.Setup(s => s.RevokeSessionAsync(SessionId, It.IsAny<string>())).ReturnsAsync(false);

            var result = await CreateController().RevokeSession(new RevokeSessionRequest { Reason = "x" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task RevokeSession_Success_DefaultReason_ReturnsOkAndDeletesCookie()
        {
            _sessionService.Setup(s => s.RevokeSessionAsync(SessionId, "user_logout")).ReturnsAsync(true);

            var controller = CreateController();
            var result = await controller.RevokeSession(null);

            result.Should().BeOfType<OkObjectResult>();
            _sessionService.Verify(s => s.RevokeSessionAsync(SessionId, "user_logout"), Times.Once);
            controller.Response.Headers.SetCookie.ToString().Should().Contain("idp_session_id_");
        }
    }
}
