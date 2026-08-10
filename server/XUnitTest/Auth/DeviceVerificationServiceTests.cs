using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Cryptography;

namespace XUnitTest.Auth
{
    public class DeviceVerificationServiceTests
    {
        private readonly Mock<IDeviceAuthorizationRepository> _repo = new();
        private readonly Mock<IIdpSessionRepository> _sessionRepo = new();
        private readonly Mock<IAuthenticationRepository> _authRepo = new();

        private const string TenantId = "tenant-1";

        private DeviceVerificationService Create(string? publicBaseUrl = null) =>
            new(_repo.Object, _sessionRepo.Object, _authRepo.Object,
                NullLogger<DeviceVerificationService>.Instance,
                publicBaseUrl);

        private static object? Prop(object? value, string name) =>
            value?.GetType().GetProperty(name)?.GetValue(value);

        private static HttpContext Context(string? cookieHeader = null)
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Scheme = "https";
            ctx.Request.Host = new HostString("idp.example.com");
            if (cookieHeader != null)
            {
                ctx.Request.Headers["Cookie"] = cookieHeader;
            }
            return ctx;
        }

        private static HttpContext ContextWithTenant(string tenantId, string? cookieHeader = null)
        {
            var ctx = Context(cookieHeader);
            ctx.Request.Headers["X-Blocks-Key"] = tenantId;
            return ctx;
        }

        private static string SessionCookie(string sessionId, string tenantId = TenantId) =>
            $"idp_session_id_{tenantId}={sessionId}";

        private static DeviceAuthorizationRequestModel PendingEntity() => new()
        {
            Id = "req-1",
            UserCode = "ABCD-1234",
            ClientId = "client-1",
            TenantId = TenantId,
            RequestedScopes = "openid profile",
            Status = DeviceAuthorizationStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            ApprovalTokenHash = ApprovalTokenHash("approval-token"),
        };

        private static IdpSessionModel ValidSession(string tenantId = TenantId, string userId = "user-1") => new()
        {
            SessionId = "session-1",
            RevokedAt = null,
            IdleExpiry = DateTime.UtcNow.AddMinutes(10),
            AbsoluteExpiry = DateTime.UtcNow.AddHours(1),
            Accounts = new List<IdpSessionAccount>
            {
                new() { UserId = userId, TenantId = tenantId, DisplayName = "User One" }
            }
        };

        private static string ApprovalTokenHash(string token) =>
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

        // ---------- VerifyAsync ----------

        [Fact]
        public async Task VerifyAsync_ReturnsInvalidRequest_WhenRequestNull()
        {
            var result = await Create().VerifyAsync(null!, Context());

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_request");
        }

        [Fact]
        public async Task VerifyAsync_ReturnsInvalidRequest_WhenUserCodeBlank()
        {
            var result = await Create().VerifyAsync(new DeviceVerifyRequest { UserCode = "  " }, Context());

            result.Should().BeOfType<BadRequestObjectResult>();
            _repo.Verify(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task VerifyAsync_ReturnsInvalidGrant_WhenEntityNotFound()
        {
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DeviceAuthorizationRequestModel)null!);

            var result = await Create().VerifyAsync(new DeviceVerifyRequest { UserCode = "abcd 1234" }, Context());

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_grant");
        }

        [Fact]
        public async Task VerifyAsync_NormalizesUserCode_LowercaseAndSpaces()
        {
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DeviceAuthorizationRequestModel)null!);

            await Create().VerifyAsync(new DeviceVerifyRequest { UserCode = "ab cd-12 34" }, Context());

            _repo.Verify(r => r.GetByUserCodeAsync("ABCD-1234", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task VerifyAsync_ReturnsExpiredToken_WhenStatusExpired()
        {
            var entity = PendingEntity();
            entity.Status = DeviceAuthorizationStatus.Expired;
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(entity);

            var result = await Create().VerifyAsync(new DeviceVerifyRequest { UserCode = entity.UserCode }, Context());

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("expired_token");
        }

        [Fact]
        public async Task VerifyAsync_ReturnsExpiredToken_WhenPastExpiresAt()
        {
            var entity = PendingEntity();
            entity.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(entity);

            var result = await Create().VerifyAsync(new DeviceVerifyRequest { UserCode = entity.UserCode }, Context());

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("expired_token");
        }

        [Fact]
        public async Task VerifyAsync_ReturnsInvalidGrant_WhenNotPending()
        {
            var entity = PendingEntity();
            entity.Status = DeviceAuthorizationStatus.Approved;
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(entity);

            var result = await Create().VerifyAsync(new DeviceVerifyRequest { UserCode = entity.UserCode }, Context());

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_grant");
        }

        [Fact]
        public async Task VerifyAsync_ReturnsLoginRequired_WhenNoSessionCookie()
        {
            var entity = PendingEntity();
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(entity);

            var result = await Create().VerifyAsync(new DeviceVerifyRequest { UserCode = entity.UserCode }, Context());

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var body = ok.Value.Should().BeOfType<DeviceVerifyResponse>().Subject;
            body.Status.Should().Be(DeviceVerifyStatus.LoginRequired);
            body.ReturnUrl.Should().Contain("/oidc/login").And.Contain("client_id=client-1");
        }

        [Fact]
        public async Task VerifyAsync_ReturnsInvalidGrant_WhenPresentedTenantDoesNotMatchCodeTenant()
        {
            var entity = PendingEntity();
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(entity);

            var result = await Create().VerifyAsync(
                new DeviceVerifyRequest { UserCode = entity.UserCode },
                ContextWithTenant("other-tenant"));

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_grant");
        }

        [Fact]
        public async Task VerifyAsync_ReturnsLoginRequired_WhenSessionNotFound()
        {
            var entity = PendingEntity();
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            _sessionRepo.Setup(s => s.GetBySessionIdAsync(It.IsAny<string>())).ReturnsAsync((IdpSessionModel)null!);

            var result = await Create().VerifyAsync(new DeviceVerifyRequest { UserCode = entity.UserCode },
                Context(SessionCookie("session-1")));

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ((DeviceVerifyResponse)ok.Value!).Status.Should().Be(DeviceVerifyStatus.LoginRequired);
        }

        [Fact]
        public async Task VerifyAsync_ReturnsLoginRequired_WhenSessionRevoked()
        {
            var entity = PendingEntity();
            var session = ValidSession();
            session.RevokedAt = DateTime.UtcNow;
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            _sessionRepo.Setup(s => s.GetBySessionIdAsync(It.IsAny<string>())).ReturnsAsync(session);

            var result = await Create().VerifyAsync(new DeviceVerifyRequest { UserCode = entity.UserCode },
                Context(SessionCookie("session-1")));

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ((DeviceVerifyResponse)ok.Value!).Status.Should().Be(DeviceVerifyStatus.LoginRequired);
        }

        [Fact]
        public async Task VerifyAsync_ReturnsLoginRequired_WhenNoAccountForTenant()
        {
            var entity = PendingEntity();
            var session = ValidSession(tenantId: "other-tenant");
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            _sessionRepo.Setup(s => s.GetBySessionIdAsync(It.IsAny<string>())).ReturnsAsync(session);

            var result = await Create().VerifyAsync(new DeviceVerifyRequest { UserCode = entity.UserCode },
                Context(SessionCookie("session-1")));

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ((DeviceVerifyResponse)ok.Value!).Status.Should().Be(DeviceVerifyStatus.LoginRequired);
        }

        [Fact]
        public async Task VerifyAsync_ReturnsReady_WithClientNameFromRegistration()
        {
            var entity = PendingEntity();
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            _sessionRepo.Setup(s => s.GetBySessionIdAsync(It.IsAny<string>())).ReturnsAsync(ValidSession());
            _repo.Setup(r => r.SetApprovalTokenHashAsync(entity.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _authRepo.Setup(a => a.GetOidcClientRegistrationAsync("client-1"))
                .ReturnsAsync(new OidcClientRegistration { ClientId = "client-1", ClientName = "My App" });

            var result = await Create().VerifyAsync(new DeviceVerifyRequest { UserCode = entity.UserCode },
                Context(SessionCookie("session-1")));

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var body = ok.Value.Should().BeOfType<DeviceVerifyResponse>().Subject;
            body.Status.Should().Be(DeviceVerifyStatus.Ready);
            body.Payload.Should().NotBeNull();
            body.Payload!.ClientName.Should().Be("My App");
            body.Payload.Scopes.Should().BeEquivalentTo(new[] { "openid", "profile" });
            body.Payload.Tenant.Should().Be(TenantId);
            body.Payload.ApprovalToken.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task VerifyAsync_ReturnsReady_WithRequestContext()
        {
            var entity = PendingEntity();
            entity.IpAddress = "203.0.113.10";
            entity.UserAgent = "DeviceClient/1.0";
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            _sessionRepo.Setup(s => s.GetBySessionIdAsync(It.IsAny<string>())).ReturnsAsync(ValidSession());
            _repo.Setup(r => r.SetApprovalTokenHashAsync(entity.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var result = await Create().VerifyAsync(new DeviceVerifyRequest { UserCode = entity.UserCode },
                Context(SessionCookie("session-1")));

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var body = ok.Value.Should().BeOfType<DeviceVerifyResponse>().Subject;
            body.Payload!.RequestIpAddress.Should().Be("203.0.113.10");
            body.Payload.RequestUserAgent.Should().Be("DeviceClient/1.0");
        }

        [Fact]
        public async Task VerifyAsync_ReturnsReady_FallsBackToClientId_WhenRegistrationNull()
        {
            var entity = PendingEntity();
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            _sessionRepo.Setup(s => s.GetBySessionIdAsync(It.IsAny<string>())).ReturnsAsync(ValidSession());
            _repo.Setup(r => r.SetApprovalTokenHashAsync(entity.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _authRepo.Setup(a => a.GetOidcClientRegistrationAsync(It.IsAny<string>()))
                .ReturnsAsync((OidcClientRegistration)null!);

            var result = await Create().VerifyAsync(new DeviceVerifyRequest { UserCode = entity.UserCode },
                Context(SessionCookie("session-1")));

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ((DeviceVerifyResponse)ok.Value!).Payload!.ClientName.Should().Be("client-1");
        }

        // ---------- DecisionAsync ----------

        [Fact]
        public async Task DecisionAsync_ReturnsInvalidRequest_WhenRequestNull()
        {
            var result = await Create().DecisionAsync(null!, Context());

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_request");
        }

        [Fact]
        public async Task DecisionAsync_Returns400Object_WhenEntityNotFound()
        {
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DeviceAuthorizationRequestModel)null!);

            var result = await Create().DecisionAsync(
                new DeviceDecisionRequest { UserCode = "ABCD-1234", Decision = "allow", ApprovalToken = "approval-token" }, Context());

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            Prop(obj.Value, "error").Should().Be("invalid_grant");
        }

        [Fact]
        public async Task DecisionAsync_ReturnsInvalidRequest_WhenDecisionNotAllowOrDeny()
        {
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(PendingEntity());

            var result = await Create().DecisionAsync(
                new DeviceDecisionRequest { UserCode = "ABCD-1234", Decision = "maybe" }, Context());

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_request");
        }

        [Fact]
        public async Task DecisionAsync_ReturnsExpiredToken_WhenExpired()
        {
            var entity = PendingEntity();
            entity.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(entity);

            var result = await Create().DecisionAsync(
                new DeviceDecisionRequest { UserCode = entity.UserCode, Decision = "allow" }, Context());

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("expired_token");
        }

        [Fact]
        public async Task DecisionAsync_Returns410_WhenNotPending()
        {
            var entity = PendingEntity();
            entity.Status = DeviceAuthorizationStatus.Denied;
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(entity);

            var result = await Create().DecisionAsync(
                new DeviceDecisionRequest { UserCode = entity.UserCode, Decision = "allow" }, Context());

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status410Gone);
            Prop(obj.Value, "error").Should().Be("request_not_pending");
        }

        [Fact]
        public async Task DecisionAsync_Returns401_WhenNoSessionCookie()
        {
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(PendingEntity());

            var result = await Create().DecisionAsync(
                new DeviceDecisionRequest { UserCode = "ABCD-1234", Decision = "allow", ApprovalToken = "approval-token" }, Context());

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            Prop(obj.Value, "error").Should().Be("login_required");
        }

        [Fact]
        public async Task DecisionAsync_Returns401_WhenNoMatchingApprover()
        {
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(PendingEntity());
            _sessionRepo.Setup(s => s.GetBySessionIdAsync(It.IsAny<string>()))
                .ReturnsAsync(ValidSession(tenantId: "other-tenant"));

            var result = await Create().DecisionAsync(
                new DeviceDecisionRequest { UserCode = "ABCD-1234", Decision = "allow", ApprovalToken = "approval-token" },
                Context(SessionCookie("session-1")));

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }

        [Fact]
        public async Task DecisionAsync_Returns401_WhenSessionRevoked()
        {
            var session = ValidSession();
            session.RevokedAt = DateTime.UtcNow;
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(PendingEntity());
            _sessionRepo.Setup(s => s.GetBySessionIdAsync(It.IsAny<string>()))
                .ReturnsAsync(session);

            var result = await Create().DecisionAsync(
                new DeviceDecisionRequest { UserCode = "ABCD-1234", Decision = "allow", ApprovalToken = "approval-token" },
                Context(SessionCookie("session-1")));

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            _repo.Verify(r => r.MarkApprovedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task DecisionAsync_Approves_WhenAllowAndCasSucceeds()
        {
            var entity = PendingEntity();
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            _sessionRepo.Setup(s => s.GetBySessionIdAsync(It.IsAny<string>()))
                .ReturnsAsync(ValidSession(userId: "approver-1"));
            _repo.Setup(r => r.MarkApprovedAsync(entity.Id, "approver-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var result = await Create().DecisionAsync(
                new DeviceDecisionRequest { UserCode = entity.UserCode, Decision = "allow", ApprovalToken = "approval-token" },
                Context(SessionCookie("session-1")));

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var body = ok.Value.Should().BeOfType<DeviceApproveResponse>().Subject;
            body.Status.Should().Be(DeviceAuthorizationStatus.Approved);
            body.Redirect.Should().Contain("outcome=approved");
            _repo.Verify(r => r.MarkApprovedAsync(entity.Id, "approver-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DecisionAsync_Denies_WhenDenyAndCasSucceeds()
        {
            var entity = PendingEntity();
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            _sessionRepo.Setup(s => s.GetBySessionIdAsync(It.IsAny<string>())).ReturnsAsync(ValidSession());
            _repo.Setup(r => r.MarkDeniedAsync(entity.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var result = await Create().DecisionAsync(
                new DeviceDecisionRequest { UserCode = entity.UserCode, Decision = "DENY", ApprovalToken = "approval-token" },
                Context(SessionCookie("session-1")));

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var body = ok.Value.Should().BeOfType<DeviceApproveResponse>().Subject;
            body.Status.Should().Be(DeviceAuthorizationStatus.Denied);
            body.Redirect.Should().Contain("outcome=denied");
            _repo.Verify(r => r.MarkDeniedAsync(entity.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DecisionAsync_Returns410_WhenCasFails()
        {
            var entity = PendingEntity();
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            _sessionRepo.Setup(s => s.GetBySessionIdAsync(It.IsAny<string>())).ReturnsAsync(ValidSession());
            _repo.Setup(r => r.MarkApprovedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var result = await Create().DecisionAsync(
                new DeviceDecisionRequest { UserCode = entity.UserCode, Decision = "allow", ApprovalToken = "approval-token" },
                Context(SessionCookie("session-1")));

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status410Gone);
            Prop(obj.Value, "error").Should().Be("request_not_pending");
        }

        // ---------- EntryAsync ----------

        [Fact]
        public void EntryAsync_ReturnsDeviceRedirect_WhenJsonAccepted()
        {
            var ctx = Context();
            ctx.Request.Headers["Accept"] = "application/json";

            var result = Create().EntryAsync(ctx.Request);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "redirect").Should().Be("/device");
        }

        [Fact]
        public void EntryAsync_ReturnsDeviceRedirect_WhenNoAcceptHeader()
        {
            var result = Create().EntryAsync(Context().Request);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "redirect").Should().Be("/device");
        }
    }
}
