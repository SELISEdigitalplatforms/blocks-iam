using Authentication.DomainService.Authentication;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using Authentication.DomainService.Utilities;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Dtos;
using Iam.DomainService.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using System.Text.Json;

namespace XUnitTest.Auth
{
    /// <summary>
    /// Covers the impersonation, cookie, session-cookie and pass-through methods of
    /// <see cref="AuthenticationService"/> that <c>AuthenticationServiceTests</c> does not touch:
    /// ExecuteImpersonateAsync (+ org-switch), ExecuteStopImpersonationAsync, CookieToken,
    /// DeleteCookie, AppendSessionCookies, ClearIdpSessionCookie, LogoutUser, ProcessTimeline,
    /// EnsureIdpSessionForOidcCallbackAsync happy paths, identity-provider create/update/rotate
    /// pass-throughs, and TriggerBackchannelLogoutAllAsync with a configured (unreachable) URI.
    /// </summary>
    public class AuthenticationServiceImpersonationTests : IDisposable
    {
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IAuthenticationDomainService> _domain = new();
        private readonly Mock<IAuthSessionFacade> _session = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();
        private readonly Mock<IRefreshTokenRepository> _refresh = new();

        private const string TenantId = "tenant-1";
        private const string ActorId = "actor-1";
        private const string TargetTenantId = "target-1";
        private const string AppOrigin = "https://app.example.com";

        public AuthenticationServiceImpersonationTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: TenantId, roles: null, userId: ActorId, impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: TenantId, impersonationSessionId: null, applicationDomain: "test"));
            _activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);
            _session.Setup(s => s.RevokeRefreshToken(It.IsAny<string>())).Returns(Task.CompletedTask);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private AuthenticationService Create() =>
            new(NullLogger<AuthenticationService>.Instance, _cache.Object, _repo.Object, _domain.Object,
                _session.Object, _tenants.Object, _activity.Object, _refresh.Object);

        // ---------------- helpers ----------------

        private static Tenant RootTenant(bool root = true, bool withApps = false) => new()
        {
            TenantId = TenantId,
            IsRootTenant = root,
            DbConnectionString = string.Empty,
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow },
            Applications = withApps
                ? new List<Applications> { new() { Domain = AppOrigin, CookieDomain = ".example.com" } }
                : new List<Applications>()
        };

        private static Tenant PlainTenant(string id) => new()
        {
            TenantId = id,
            DbConnectionString = string.Empty,
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow },
            Applications = new List<Applications>()
        };

        private static DefaultHttpContext HttpContext(string? origin = null, string? cookieHeader = null)
        {
            var ctx = new DefaultHttpContext();
            if (origin != null) ctx.Request.Headers["Origin"] = origin;
            if (cookieHeader != null) ctx.Request.Headers["Cookie"] = cookieHeader;
            return ctx;
        }

        private static Mock<IMongoCollection<BsonDocument>> MockProjectPeoples(bool shared)
        {
            var list = shared ? new List<BsonDocument> { new() } : new List<BsonDocument>();
            var cursor = new Mock<IAsyncCursor<BsonDocument>>();
            cursor.Setup(c => c.Current).Returns(list);
            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(list.Count > 0).Returns(false);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(list.Count > 0).ReturnsAsync(false);

            var collection = new Mock<IMongoCollection<BsonDocument>>();
            collection.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor.Object);
            return collection;
        }

        /// <summary>Sets up the mocks so ValidateImpersonationRequestAsync passes for actor-1 -> target-1.</summary>
        private void SetupValidationPasses(Tenant rootTenant, bool shared = true)
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(rootTenant);
            _tenants.Setup(t => t.GetTenantByID(TargetTenantId)).Returns(PlainTenant(TargetTenantId));
            _repo.Setup(r => r.GetUserByIdAsync(ActorId)).ReturnsAsync(new User { ItemId = ActorId, Email = "a@b.com" });
            _repo.Setup(r => r.GetCollectionByName<BsonDocument>("ProjectPeoples")).Returns(MockProjectPeoples(shared).Object);
        }

        private static RefreshTokenCache ValidRootCache(string clientId = "c1") => new()
        {
            ClientId = clientId,
            RefreshToken = "root-rt",
            ExpiresUtc = DateTime.UtcNow.AddHours(1),
            Impersonated = false
        };

        private static string? ErrorOf(IActionResult result)
        {
            var value = (result as ObjectResult)?.Value;
            return value?.GetType().GetProperty("error")?.GetValue(value) as string;
        }

        // ============ ExecuteImpersonateAsync — validation branches ============

        [Fact]
        public async Task ExecuteImpersonate_RootTenantMissing_ReturnsForbidden()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns((Tenant)null!);
            var req = new ImpersonateRequest { TargetTenantId = TargetTenantId };

            var result = await Create().ExecuteImpersonateAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        }

        [Fact]
        public async Task ExecuteImpersonate_NonRootTenant_ReturnsForbidden()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(RootTenant(root: false));
            var req = new ImpersonateRequest { TargetTenantId = TargetTenantId };

            var result = await Create().ExecuteImpersonateAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        }

        [Fact]
        public async Task ExecuteImpersonate_MissingTargetTenant_ReturnsBadRequest()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(RootTenant());
            var req = new ImpersonateRequest { TargetTenantId = "" };

            var result = await Create().ExecuteImpersonateAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<BadRequestObjectResult>();
            ErrorOf(result).Should().Be("invalid_request");
        }

        [Fact]
        public async Task ExecuteImpersonate_TargetTenantNotFound_ReturnsBadRequest()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(RootTenant());
            _tenants.Setup(t => t.GetTenantByID(TargetTenantId)).Returns((Tenant)null!);
            var req = new ImpersonateRequest { TargetTenantId = TargetTenantId };

            var result = await Create().ExecuteImpersonateAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<BadRequestObjectResult>();
            ErrorOf(result).Should().Be("invalid_target_tenant");
        }

        [Fact]
        public async Task ExecuteImpersonate_UserNotFound_ReturnsUnauthorized()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(RootTenant());
            _tenants.Setup(t => t.GetTenantByID(TargetTenantId)).Returns(PlainTenant(TargetTenantId));
            _repo.Setup(r => r.GetUserByIdAsync(ActorId)).ReturnsAsync((User)null!);
            var req = new ImpersonateRequest { TargetTenantId = TargetTenantId };

            var result = await Create().ExecuteImpersonateAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task ExecuteImpersonate_NotSharedWithUser_ReturnsForbidden()
        {
            SetupValidationPasses(RootTenant(), shared: false);
            var req = new ImpersonateRequest { TargetTenantId = TargetTenantId };

            var result = await Create().ExecuteImpersonateAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        }

        // ============ ExecuteImpersonateAsync — refresh/session branches ============

        [Fact]
        public async Task ExecuteImpersonate_NoRefreshToken_ReturnsSessionExpired()
        {
            SetupValidationPasses(RootTenant()); // no apps => CookieToken cannot resolve
            var req = new ImpersonateRequest { TargetTenantId = TargetTenantId, RefreshToken = "" };

            var result = await Create().ExecuteImpersonateAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<UnauthorizedObjectResult>();
            ErrorOf(result).Should().Be(OAuthError.SessionExpired);
        }

        [Fact]
        public async Task ExecuteImpersonate_RefreshCacheMissing_ReturnsSessionExpired()
        {
            SetupValidationPasses(RootTenant());
            _cache.Setup(c => c.GetStringValueAsync("root-rt")).ReturnsAsync((string)null!);
            var req = new ImpersonateRequest { TargetTenantId = TargetTenantId, RefreshToken = "root-rt" };

            var result = await Create().ExecuteImpersonateAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<UnauthorizedObjectResult>();
            ErrorOf(result).Should().Be(OAuthError.SessionExpired);
        }

        [Fact]
        public async Task ExecuteImpersonate_RefreshCacheExpired_ReturnsSessionExpired()
        {
            SetupValidationPasses(RootTenant());
            var expired = ValidRootCache();
            expired.ExpiresUtc = DateTime.UtcNow.AddMinutes(-5);
            _cache.Setup(c => c.GetStringValueAsync("root-rt")).ReturnsAsync(JsonSerializer.Serialize(expired));
            var req = new ImpersonateRequest { TargetTenantId = TargetTenantId, RefreshToken = "root-rt" };

            var result = await Create().ExecuteImpersonateAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<UnauthorizedObjectResult>();
            ErrorOf(result).Should().Be(OAuthError.SessionExpired);
        }

        [Fact]
        public async Task ExecuteImpersonate_InvalidClient_ReturnsUnauthorized()
        {
            SetupValidationPasses(RootTenant());
            _cache.Setup(c => c.GetStringValueAsync("root-rt")).ReturnsAsync(JsonSerializer.Serialize(ValidRootCache()));
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync((OidcClientRegistration)null!);
            var req = new ImpersonateRequest { TargetTenantId = TargetTenantId, RefreshToken = "root-rt" };

            var result = await Create().ExecuteImpersonateAsync(req, HttpContext().Request, HttpContext().Response);

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            ErrorOf(result).Should().Be("invalid_client");
        }

        [Fact]
        public async Task ExecuteImpersonate_SessionCreationThrows_ReturnsServerError()
        {
            SetupValidationPasses(RootTenant());
            _cache.Setup(c => c.GetStringValueAsync("root-rt")).ReturnsAsync(JsonSerializer.Serialize(ValidRootCache()));
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(new OidcClientRegistration { ItemId = "c1", ClientId = "c1" });
            _session.Setup(s => s.CreateAndBackupImpersonationSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("insert failed"));
            var req = new ImpersonateRequest { TargetTenantId = TargetTenantId, RefreshToken = "root-rt" };

            var result = await Create().ExecuteImpersonateAsync(req, HttpContext().Request, HttpContext().Response);

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
            ErrorOf(result).Should().Be(OAuthError.SessionCreationFailed);
        }

        [Fact]
        public async Task ExecuteImpersonate_HappyPath_NoCookies_ReturnsTokens()
        {
            SetupValidationPasses(RootTenant()); // no apps => AppendCookies returns false
            _cache.Setup(c => c.GetStringValueAsync("root-rt")).ReturnsAsync(JsonSerializer.Serialize(ValidRootCache()));
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(new OidcClientRegistration { ItemId = "c1", ClientId = "c1" });
            _session.Setup(s => s.CreateAndBackupImpersonationSessionAsync(ActorId, TenantId, TargetTenantId, "c1", "default"))
                .ReturnsAsync("sess-new");
            _session.Setup(s => s.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "at", RefreshToken = "rt", TokenType = "Bearer", ExpiresIn = 3600 });
            var req = new ImpersonateRequest { TargetTenantId = TargetTenantId, RefreshToken = "root-rt" };

            var result = await Create().ExecuteImpersonateAsync(req, HttpContext().Request, HttpContext().Response);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var payload = ok.Value.Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
            payload["impersonation_session_id"].Should().Be("sess-new");
            payload["cookie_set"].Should().Be(false);
            payload["access_token"].Should().Be("at");
            _session.Verify(s => s.CreateAndBackupImpersonationSessionAsync(ActorId, TenantId, TargetTenantId, "c1", "default"), Times.Once);
            _session.Verify(s => s.RevokeRefreshToken("root-rt"), Times.Once);
        }

        [Fact]
        public async Task ExecuteImpersonate_HappyPath_WithResolvedDomain_SetsCookies()
        {
            SetupValidationPasses(RootTenant(withApps: true)); // apps + Origin => AppendCookies true
            _cache.Setup(c => c.GetStringValueAsync("root-rt")).ReturnsAsync(JsonSerializer.Serialize(ValidRootCache()));
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(new OidcClientRegistration { ItemId = "c1", ClientId = "c1" });
            _session.Setup(s => s.CreateAndBackupImpersonationSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("sess-new");
            _session.Setup(s => s.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "at", RefreshToken = "rt", TokenType = "Bearer", ExpiresIn = 3600, ExpiresUtc = DateTime.UtcNow.AddHours(1), RefreshExpiresUtc = DateTime.UtcNow.AddDays(1) });

            var ctx = HttpContext(origin: AppOrigin);
            var req = new ImpersonateRequest { TargetTenantId = TargetTenantId, RefreshToken = "root-rt" };

            var result = await Create().ExecuteImpersonateAsync(req, ctx.Request, ctx.Response);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var payload = ok.Value.Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
            payload["impersonation_mode"].Should().Be(true);
            payload["cookie_set"].Should().Be(true);
            payload.Should().NotContainKey("access_token");
            ctx.Response.Headers.Should().ContainKey("Set-Cookie");
        }

        // ============ ExecuteImpersonateAsync — organization switch path ============

        [Fact]
        public async Task ExecuteImpersonate_OrgSwitch_Success_ReturnsOk()
        {
            SetupValidationPasses(RootTenant());
            _cache.Setup(c => c.GetStringValueAsync("root-rt")).ReturnsAsync(JsonSerializer.Serialize(ValidRootCache()));
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _repo.Setup(r => r.GetImpersonationSessionByIdAsync("imp-1"))
                .ReturnsAsync(new ImpersonationSession { Id = "imp-1", Status = "active", TargetTenantId = TargetTenantId });
            _session.Setup(s => s.SwitchOrganizationContextAsync("imp-1", "org-2")).ReturnsAsync(true);
            _session.Setup(s => s.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "at2", RefreshToken = "rt2", TokenType = "Bearer" });
            var req = new ImpersonateRequest { TargetTenantId = TargetTenantId, RefreshToken = "root-rt", ImpersonationId = "imp-1", OrganizationId = "org-2" };

            var result = await Create().ExecuteImpersonateAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<OkObjectResult>();
            _session.Verify(s => s.SwitchOrganizationContextAsync("imp-1", "org-2"), Times.Once);
            _session.Verify(s => s.CreateAndBackupImpersonationSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteImpersonate_OrgSwitch_TokenError_ReturnsServerError()
        {
            SetupValidationPasses(RootTenant());
            _cache.Setup(c => c.GetStringValueAsync("root-rt")).ReturnsAsync(JsonSerializer.Serialize(ValidRootCache()));
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _repo.Setup(r => r.GetImpersonationSessionByIdAsync("imp-1"))
                .ReturnsAsync(new ImpersonationSession { Id = "imp-1", Status = "active", TargetTenantId = TargetTenantId });
            _session.Setup(s => s.SwitchOrganizationContextAsync("imp-1", "org-2")).ReturnsAsync(true);
            _session.Setup(s => s.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { Error = "server_error" });
            var req = new ImpersonateRequest { TargetTenantId = TargetTenantId, RefreshToken = "root-rt", ImpersonationId = "imp-1", OrganizationId = "org-2" };

            var result = await Create().ExecuteImpersonateAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        }

        [Fact]
        public async Task ExecuteImpersonate_OrgSwitch_TargetMismatch_FallsThroughToMainPath()
        {
            SetupValidationPasses(RootTenant());
            _cache.Setup(c => c.GetStringValueAsync("root-rt")).ReturnsAsync(JsonSerializer.Serialize(ValidRootCache()));
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            // Existing session targets a DIFFERENT tenant => TrySwitch returns null and flow continues to main impersonation.
            _repo.Setup(r => r.GetImpersonationSessionByIdAsync("imp-1"))
                .ReturnsAsync(new ImpersonationSession { Id = "imp-1", Status = "active", TargetTenantId = "other-tenant" });
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(new OidcClientRegistration { ItemId = "c1", ClientId = "c1" });
            _session.Setup(s => s.CreateAndBackupImpersonationSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("sess-new");
            _session.Setup(s => s.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "at", TokenType = "Bearer" });
            var req = new ImpersonateRequest { TargetTenantId = TargetTenantId, RefreshToken = "root-rt", ImpersonationId = "imp-1" };

            var result = await Create().ExecuteImpersonateAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<OkObjectResult>();
            _session.Verify(s => s.SwitchOrganizationContextAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _session.Verify(s => s.CreateAndBackupImpersonationSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        // ============ ExecuteStopImpersonationAsync ============

        [Fact]
        public async Task ExecuteStop_SessionNotFound_ReturnsUnauthorized()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(RootTenant());
            var req = new StopImpersonationRequest(); // no impersonation id => session stays null

            var result = await Create().ExecuteStopImpersonationAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<UnauthorizedObjectResult>();
            ErrorOf(result).Should().Be(OAuthError.SessionExpired);
            _activity.Verify(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task ExecuteStop_NoRefreshToken_ReturnsUnauthorized()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(RootTenant());
            _repo.Setup(r => r.GetImpersonationSessionByIdAsync("imp-1"))
                .ReturnsAsync(new ImpersonationSession { Id = "imp-1", Status = "active", ClientId = "c1", OrganizationId = "org", RootTenantId = TenantId, TargetTenantId = TargetTenantId });
            var req = new StopImpersonationRequest { ImpersonationId = "imp-1", RefreshToken = "" };

            var result = await Create().ExecuteStopImpersonationAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<UnauthorizedObjectResult>();
            ErrorOf(result).Should().Be("invalid_refresh_token");
        }

        [Fact]
        public async Task ExecuteStop_CacheNotImpersonated_ReturnsUnauthorized()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(RootTenant());
            _repo.Setup(r => r.GetImpersonationSessionByIdAsync("imp-1"))
                .ReturnsAsync(new ImpersonationSession { Id = "imp-1", Status = "active", ClientId = "c1", OrganizationId = "org", RootTenantId = TenantId, TargetTenantId = TargetTenantId });
            _cache.Setup(c => c.GetStringValueAsync("imp-rt")).ReturnsAsync(JsonSerializer.Serialize(new RefreshTokenCache { Impersonated = false }));
            var req = new StopImpersonationRequest { ImpersonationId = "imp-1", RefreshToken = "imp-rt" };

            var result = await Create().ExecuteStopImpersonationAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<UnauthorizedObjectResult>();
            ErrorOf(result).Should().Be("invalid_refresh_token");
        }

        [Fact]
        public async Task ExecuteStop_RootUserNotFound_ReturnsBadRequest()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(RootTenant());
            _repo.Setup(r => r.GetImpersonationSessionByIdAsync("imp-1"))
                .ReturnsAsync(new ImpersonationSession { Id = "imp-1", Status = "active", ClientId = "c1", OrganizationId = "org", RootTenantId = TenantId, TargetTenantId = TargetTenantId });
            _cache.Setup(c => c.GetStringValueAsync("imp-rt")).ReturnsAsync(JsonSerializer.Serialize(new RefreshTokenCache { Impersonated = true, ClientId = "c1" }));
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _repo.Setup(r => r.GetUserByIdAsync(ActorId)).ReturnsAsync((User)null!);
            var req = new StopImpersonationRequest { ImpersonationId = "imp-1", RefreshToken = "imp-rt" };

            var result = await Create().ExecuteStopImpersonationAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task ExecuteStop_TokenError_ReturnsBadRequest()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(RootTenant());
            _repo.Setup(r => r.GetImpersonationSessionByIdAsync("imp-1"))
                .ReturnsAsync(new ImpersonationSession { Id = "imp-1", Status = "active", ClientId = "c1", OrganizationId = "org", RootTenantId = TenantId, TargetTenantId = TargetTenantId });
            _cache.Setup(c => c.GetStringValueAsync("imp-rt")).ReturnsAsync(JsonSerializer.Serialize(new RefreshTokenCache { Impersonated = true, ClientId = "c1" }));
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _repo.Setup(r => r.GetUserByIdAsync(ActorId)).ReturnsAsync(new User { ItemId = ActorId });
            _session.Setup(s => s.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { Error = "invalid_grant" });
            var req = new StopImpersonationRequest { ImpersonationId = "imp-1", RefreshToken = "imp-rt" };

            var result = await Create().ExecuteStopImpersonationAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<BadRequestObjectResult>();
            ErrorOf(result).Should().Be("invalid_grant");
        }

        [Fact]
        public async Task ExecuteStop_HappyPath_NoCookies_ReturnsTokens_AndEndsSession()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(RootTenant());
            _repo.Setup(r => r.GetImpersonationSessionByIdAsync("imp-1"))
                .ReturnsAsync(new ImpersonationSession { Id = "imp-1", Status = "active", ClientId = "c1", OrganizationId = "org", RootTenantId = TenantId, TargetTenantId = TargetTenantId });
            _cache.Setup(c => c.GetStringValueAsync("imp-rt")).ReturnsAsync(JsonSerializer.Serialize(new RefreshTokenCache { Impersonated = true, ClientId = "c1" }));
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _repo.Setup(r => r.GetUserByIdAsync(ActorId)).ReturnsAsync(new User { ItemId = ActorId });
            _repo.Setup(r => r.UpdateImpersonationSessionAsync("imp-1", It.IsAny<Dictionary<string, object>>())).ReturnsAsync(true);
            _session.Setup(s => s.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "root-at", RefreshToken = "root-rt2", TokenType = "Bearer" });
            var req = new StopImpersonationRequest { ImpersonationId = "imp-1", RefreshToken = "imp-rt" };

            var result = await Create().ExecuteStopImpersonationAsync(req, HttpContext().Request, HttpContext().Response);

            result.Should().BeOfType<OkObjectResult>();
            _repo.Verify(r => r.UpdateImpersonationSessionAsync("imp-1", It.IsAny<Dictionary<string, object>>()), Times.Once);
            _session.Verify(s => s.RevokeRefreshToken("imp-rt"), Times.Once);
        }

        // ============ CookieToken ============

        [Fact]
        public void CookieToken_ResolvedDomain_ReturnsTokenFromCookie()
        {
            var tenant = RootTenant(withApps: true);
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(tenant);

            var probe = HttpContext(origin: AppOrigin);
            var (domain, _, resolved) = DomainResolver.ResolveDomain(tenant, probe.Request);
            resolved.Should().BeTrue();

            var ctx = HttpContext(origin: AppOrigin, cookieHeader: $"rt_{domain}=the-token");

            var result = Create().CookieToken(ctx.Request);

            result.Should().Be("the-token");
        }

        [Fact]
        public void CookieToken_UnresolvedDomain_ReturnsEmpty()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(RootTenant()); // no apps

            var result = Create().CookieToken(HttpContext(origin: AppOrigin).Request);

            result.Should().BeEmpty();
        }

        // ============ DeleteCookie ============

        [Fact]
        public void DeleteCookie_ResolvedDomain_ReturnsTrue_AndWritesDeletionCookies()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(RootTenant(withApps: true));
            var ctx = HttpContext(origin: AppOrigin);

            var result = Create().DeleteCookie(ctx.Request);

            result.Should().BeTrue();
            ctx.Response.Headers.Should().ContainKey("Set-Cookie");
        }

        // ============ AppendSessionCookies ============

        [Fact]
        public async Task AppendSessionCookies_UnresolvedDomain_WritesNoCookies()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(RootTenant()); // no apps
            var ctx = HttpContext(origin: AppOrigin);

            await Create().AppendSessionCookies(ctx, "at", "rt");

            ctx.Response.Headers.Should().NotContainKey("Set-Cookie");
        }

        [Fact]
        public async Task AppendSessionCookies_ResolvedDomain_WritesCookies()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(RootTenant(withApps: true));
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync())
                .ReturnsAsync(new IdentityConfiguration { AccessTokenValidForNumberMinutes = 15, AbsoluteRefreshTokenValidForNumberMinutes = 60 });
            var ctx = HttpContext(origin: AppOrigin);

            await Create().AppendSessionCookies(ctx, "at", "rt");

            ctx.Response.Headers.Should().ContainKey("Set-Cookie");
        }

        // ============ ClearIdpSessionCookie ============

        [Fact]
        public void ClearIdpSessionCookie_WritesDeletionCookie()
        {
            var ctx = new DefaultHttpContext();

            Create().ClearIdpSessionCookie(ctx.Response);

            ctx.Response.Headers.Should().ContainKey("Set-Cookie");
        }

        // ============ LogoutUser / ProcessTimeline ============

        [Fact]
        public async Task LogoutUser_NoCache_ReturnsFailure_AndDispatchesTimeline()
        {
            _cache.Setup(c => c.GetStringValueAsync("rt")).ReturnsAsync((string)null!);
            _domain.Setup(d => d.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation)null!);
            _domain.Setup(d => d.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(new List<string> { "1.2.3.4" });

            var result = await Create().LogoutUser("rt", new DefaultHttpContext().Request);

            result.IsSuccess.Should().BeFalse();
            _activity.Verify(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Once);
        }

        [Fact]
        public async Task ProcessTimeline_DispatchesActivity_ReturnsTrue()
        {
            _domain.Setup(d => d.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation)null!);
            _domain.Setup(d => d.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(new List<string> { "9.9.9.9" });

            var result = await Create().ProcessTimeline(new DefaultHttpContext().Request, isFromAll: true);

            result.Should().BeTrue();
            _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(e => e.Event == "LOGOUT_ALL")), Times.Once);
        }

        // ============ EnsureIdpSessionForOidcCallbackAsync — happy paths ============

        private static string IdpCookieKey => IdpConstants.BuildIdpSessionCookieKey(TenantId);

        private static IdpSessionModel ValidIdpSession(string sessionId, string userId, string tenantId) => new()
        {
            SessionId = sessionId,
            TenantId = tenantId,
            Accounts = new List<IdpSessionAccount> { new() { UserId = userId, TenantId = tenantId, LoginAt = DateTime.UtcNow } },
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            IdleExpiry = DateTime.UtcNow.AddHours(1),
            AbsoluteExpiry = DateTime.UtcNow.AddDays(30)
        };

        [Fact]
        public async Task EnsureIdpSessionForOidcCallback_NoCookie_CreatesSession_ReturnsTrue()
        {
            _session.Setup(s => s.CreateSessionAsync("user-1", TenantId, It.IsAny<string>())).ReturnsAsync("new-sess");

            var ctx = new DefaultHttpContext();
            var result = await Create().EnsureIdpSessionForOidcCallbackAsync(ctx, "user-1", TenantId);

            result.Should().BeTrue();
            _session.Verify(s => s.CreateSessionAsync("user-1", TenantId, It.IsAny<string>()), Times.Once);
            ctx.Response.Headers.Should().ContainKey("Set-Cookie");
        }

        [Fact]
        public async Task EnsureIdpSessionForOidcCallback_ExistingSession_AccountExists_UpdatesActivity()
        {
            _session.Setup(s => s.GetSessionAsync("sess-1")).ReturnsAsync(ValidIdpSession("sess-1", "user-1", TenantId));
            _session.Setup(s => s.UpdateActivityAsync("sess-1")).ReturnsAsync(true);
            _session.Setup(s => s.RotateSessionAsync("sess-1", It.IsAny<string>())).ReturnsAsync("sess-rot");

            var ctx = HttpContext(cookieHeader: $"{IdpCookieKey}=sess-1");
            var result = await Create().EnsureIdpSessionForOidcCallbackAsync(ctx, "user-1", TenantId);

            result.Should().BeTrue();
            _session.Verify(s => s.UpdateActivityAsync("sess-1"), Times.Once);
            _session.Verify(s => s.AddAccountAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task EnsureIdpSessionForOidcCallback_ExistingSession_NewAccount_AddsAccount()
        {
            _session.Setup(s => s.GetSessionAsync("sess-1")).ReturnsAsync(ValidIdpSession("sess-1", "other-user", TenantId));
            _session.Setup(s => s.AddAccountAsync("sess-1", "user-1", TenantId, "user-1")).ReturnsAsync(true);
            _session.Setup(s => s.RotateSessionAsync("sess-1", It.IsAny<string>())).ReturnsAsync("sess-rot");

            var ctx = HttpContext(cookieHeader: $"{IdpCookieKey}=sess-1");
            var result = await Create().EnsureIdpSessionForOidcCallbackAsync(ctx, "user-1", TenantId);

            result.Should().BeTrue();
            _session.Verify(s => s.AddAccountAsync("sess-1", "user-1", TenantId, "user-1"), Times.Once);
        }

        [Fact]
        public async Task EnsureIdpSessionForOidcCallback_InvalidExistingSession_CreatesNew()
        {
            var revoked = ValidIdpSession("sess-1", "user-1", TenantId);
            revoked.RevokedAt = DateTime.UtcNow;
            _session.Setup(s => s.GetSessionAsync("sess-1")).ReturnsAsync(revoked);
            _session.Setup(s => s.CreateSessionAsync("user-1", TenantId, It.IsAny<string>())).ReturnsAsync("new-sess");

            var ctx = HttpContext(cookieHeader: $"{IdpCookieKey}=sess-1");
            var result = await Create().EnsureIdpSessionForOidcCallbackAsync(ctx, "user-1", TenantId);

            result.Should().BeTrue();
            _session.Verify(s => s.CreateSessionAsync("user-1", TenantId, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task EnsureIdpSessionForOidcCallback_Exception_ReturnsFalse()
        {
            _session.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("session store down"));

            var result = await Create().EnsureIdpSessionForOidcCallbackAsync(new DefaultHttpContext(), "user-1", TenantId);

            result.Should().BeFalse();
        }

        // ============ identity-provider pass-throughs ============

        [Fact]
        public async Task CreateIdentityProvider_DelegatesToDomainService()
        {
            _domain.Setup(d => d.CreateIdentityProviderAsync(It.IsAny<SaveIdentityProviderRequest>()))
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await Create().CreateIdentityProviderAsync(new SaveIdentityProviderRequest { Provider = "google" });

            result.IsSuccess.Should().BeTrue();
            _domain.Verify(d => d.CreateIdentityProviderAsync(It.IsAny<SaveIdentityProviderRequest>()), Times.Once);
        }

        [Fact]
        public async Task UpdateIdentityProvider_DelegatesToDomainService()
        {
            _domain.Setup(d => d.UpdateIdentityProviderAsync("idp-1", It.IsAny<UpdateIdentityProviderRequest>()))
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest());

            result.IsSuccess.Should().BeTrue();
            _domain.Verify(d => d.UpdateIdentityProviderAsync("idp-1", It.IsAny<UpdateIdentityProviderRequest>()), Times.Once);
        }

        [Fact]
        public async Task RotateOidcClientSecret_DelegatesToDomainService()
        {
            _domain.Setup(d => d.RotateOidcClientSecretAsync("idp-1"))
                .ReturnsAsync(new RotateOidcClientSecretResponse { ItemId = "idp-1", ClientId = "c1", ClientSecret = "new-secret" });

            var result = await Create().RotateOidcClientSecretAsync("idp-1");

            result.ClientSecret.Should().Be("new-secret");
        }

        // ============ TriggerBackchannelLogoutAllAsync — configured (unreachable) URI ============

        [Fact]
        public async Task TriggerBackchannelLogoutAll_WithUnreachableUri_ReturnsFalse_AndAudits()
        {
            _repo.Setup(r => r.GetOIDCCredentialsByTenantAsync()).ReturnsAsync(new List<OidcClientRegistration>
            {
                new() { ItemId = "c1", ClientId = "c1", BackChannelLogoutUri = "http://127.0.0.1:1/backchannel-logout" }
            });
            _domain.Setup(d => d.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(new List<string> { "1.2.3.4" });
            _domain.Setup(d => d.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation)null!);

            var result = await Create().TriggerBackchannelLogoutAllAsync(new DefaultHttpContext().Request);

            result.Should().BeFalse();
            // Each failed attempt persists a delivery audit + a security event via the dispatcher.
            _activity.Verify(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.AtLeastOnce);
        }
    }
}
