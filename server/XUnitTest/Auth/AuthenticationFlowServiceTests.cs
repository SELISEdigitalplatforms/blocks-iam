using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Shared.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Blocks.Genesis;
using Blocks.CaptchaDriver;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Idp.DomainService.Oidc.Contracts;
using Iam.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using System.Text.Json;

namespace XUnitTest.Auth
{
    public class AuthenticationFlowServiceTests : IDisposable
    {
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IAuthStrategy> _strategy = new();
        private readonly Mock<ITokenRefresher> _refresher = new();
        private readonly Mock<Authentication.DomainService.Authentication.IAuthenticationService> _authService = new();
        private readonly Mock<ICaptchaEvaluator> _captcha = new();
        private readonly Mock<IRefreshTokenRepository> _refreshRepo = new();
        private readonly Mock<IRefreshSessionResolver> _sessionResolver = new();
        private readonly Mock<IAuthenticationDomainService> _authDomain = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();
        private readonly OidcLoginAuditWriter _auditWriter;

        private const string TenantId = "tenant-1";

        public AuthenticationFlowServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: TenantId, roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: TenantId, impersonationSessionId: null, applicationDomain: "test"));

            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);
            _authDomain.Setup(d => d.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation)null!);

            _auditWriter = new OidcLoginAuditWriter(_authDomain.Object, _activity.Object, NullLogger<OidcLoginAuditWriter>.Instance);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private AuthenticationFlowService Create() =>
            new(_repo.Object, _strategy.Object, _refresher.Object, _authService.Object, _captcha.Object,
                _auditWriter, NullLogger<AuthenticationFlowService>.Instance, _refreshRepo.Object, _sessionResolver.Object);

        private static HttpRequest Req() => new DefaultHttpContext().Request;

        private static object? Prop(object? value, string name) => value?.GetType().GetProperty(name)?.GetValue(value);

        private static ClaimsPrincipal Principal(string? tenantId = TenantId, string? userId = "user-1")
        {
            var claims = new List<Claim>();
            if (tenantId != null) claims.Add(new Claim(BlocksContext.TENANT_ID_CLAIM, tenantId));
            if (userId != null) claims.Add(new Claim(BlocksContext.USER_ID_CLAIM, userId));
            return new ClaimsPrincipal(new ClaimsIdentity(claims));
        }

        // ==================== ExecuteEmbeddedLoginAsync ====================

        [Fact]
        public async Task EmbeddedLogin_ConfigMissing_Returns400()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);

            var result = await Create().ExecuteEmbeddedLoginAsync(new EmbeddedLoginRequest { Username = "u", Password = "p" }, Req());

            result.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            result.Error.Should().Be(OAuthError.AuthConfigMissing);
        }

        [Fact]
        public async Task EmbeddedLogin_AccountLocked_Returns423_AndAudits()
        {
            _repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new User { ItemId = "user-1", LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10) });

            var result = await Create().ExecuteEmbeddedLoginAsync(new EmbeddedLoginRequest { Username = "u", Password = "p" }, Req());

            result.StatusCode.Should().Be(StatusCodes.Status423Locked);
            result.Error.Should().Be(OAuthError.AccountLocked);
            _activity.Verify(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Once);
        }

        [Fact]
        public async Task EmbeddedLogin_MfaRequestWithMissingFields_ReturnsInvalidRequest()
        {
            var request = new EmbeddedLoginRequest { Username = "u", Password = "p", MfaType = UserMfaType.TOTP };

            var result = await Create().ExecuteEmbeddedLoginAsync(request, Req());

            result.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            result.Error.Should().Be("invalid_request");
        }

        [Fact]
        public async Task EmbeddedLogin_MfaVerification_HappyPath_ReturnsTokenResponse()
        {
            _repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new User { ItemId = "user-1" });
            _strategy.Setup(s => s.AuthenticateMfaAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "mfa-at" });

            var request = new EmbeddedLoginRequest { Username = "u", Password = "p", MfaId = "m1", MfaCode = "123456", MfaType = UserMfaType.TOTP };
            var result = await Create().ExecuteEmbeddedLoginAsync(request, Req());

            result.TokenResponse!.AccessToken.Should().Be("mfa-at");
            _strategy.Verify(s => s.AuthenticateMfaAsync(It.Is<TokenRequest>(t => t.GrantType == GrantTypes.MfaCode), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()), Times.Once);
        }

        [Fact]
        public async Task EmbeddedLogin_CaptchaRequired_NoCode_ReturnsCaptchaEnabled()
        {
            _repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new User { ItemId = "user-1", FailedLoginCount = CaptchaGate.FailedAttemptsBeforeCaptcha });
            _captcha.Setup(c => c.GetConfigurationAsync()).ReturnsAsync(new CaptchaConfiguration { IsEnable = true, CaptchaKey = "site-key" });

            var result = await Create().ExecuteEmbeddedLoginAsync(new EmbeddedLoginRequest { Username = "u", Password = "p" }, Req());

            result.Error.Should().Be(OAuthError.CaptchaEnabled);
            result.CaptchaRequired.Should().BeTrue();
            result.CaptchaSiteKey.Should().Be("site-key");
        }

        [Fact]
        public async Task EmbeddedLogin_CaptchaInvalid_ReturnsCaptchaInvalid_AndAudits()
        {
            _repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new User { ItemId = "user-1", FailedLoginCount = CaptchaGate.FailedAttemptsBeforeCaptcha });
            _captcha.Setup(c => c.GetConfigurationAsync()).ReturnsAsync(new CaptchaConfiguration { IsEnable = true, CaptchaKey = "site-key" });
            _captcha.Setup(c => c.VerifyAsync("wrong", It.IsAny<string>())).ReturnsAsync((object)new { Verified = false });

            var result = await Create().ExecuteEmbeddedLoginAsync(new EmbeddedLoginRequest { Username = "u", Password = "p", CaptchaCode = "wrong" }, Req());

            result.Error.Should().Be(OAuthError.CaptchaInvalid);
            result.CaptchaRequired.Should().BeTrue();
            _activity.Verify(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Once);
        }

        [Fact]
        public async Task EmbeddedLogin_PasswordSuccess_ReturnsTokens_AndAuditsSuccess()
        {
            _repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new User { ItemId = "user-1" });
            _strategy.Setup(s => s.AuthenticatePasswordAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "pwd-at" });

            var result = await Create().ExecuteEmbeddedLoginAsync(new EmbeddedLoginRequest { Username = "u", Password = "p" }, Req());

            result.TokenResponse!.AccessToken.Should().Be("pwd-at");
            _strategy.Verify(s => s.AuthenticatePasswordAsync(It.Is<TokenRequest>(t => t.GrantType == GrantTypes.Password), It.IsAny<IdentityConfiguration>()), Times.Once);
            _activity.Verify(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Once);
        }

        [Fact]
        public async Task EmbeddedLogin_PasswordInvalid_ReturnsError_AndAuditsFailure()
        {
            _repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new User { ItemId = "user-1" });
            _strategy.Setup(s => s.AuthenticatePasswordAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>()))
                .ReturnsAsync(new TokenResponse { Error = OAuthError.InValidUseNamePassword, ErrorDescription = "bad" });

            var result = await Create().ExecuteEmbeddedLoginAsync(new EmbeddedLoginRequest { Username = "u", Password = "p" }, Req());

            result.TokenResponse!.Error.Should().Be(OAuthError.InValidUseNamePassword);
            _activity.Verify(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Once);
        }

        // ==================== ExecuteSocialLoginAsync ====================

        [Fact]
        public async Task SocialLogin_ConfigMissing_Returns400()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);

            var result = await Create().ExecuteSocialLoginAsync(new SocialLoginRequest { Code = "c", State = "s", Provider = "google" }, Req());

            result.Error.Should().Be(OAuthError.AuthConfigMissing);
        }

        [Fact]
        public async Task SocialLogin_MfaVerification_CallsMfaStrategy()
        {
            _strategy.Setup(s => s.AuthenticateMfaAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "mfa-at" });

            var request = new SocialLoginRequest { MfaId = "m1", MfaCode = "123456", MfaType = UserMfaType.TOTP };
            var result = await Create().ExecuteSocialLoginAsync(request, Req());

            result.TokenResponse!.AccessToken.Should().Be("mfa-at");
            _strategy.Verify(s => s.AuthenticateMfaAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()), Times.Once);
        }

        [Fact]
        public async Task SocialLogin_MissingCode_ReturnsError()
        {
            var result = await Create().ExecuteSocialLoginAsync(new SocialLoginRequest { Code = "", State = "s", Provider = "google" }, Req());

            result.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            result.Error.Should().Be("authorization_code_missing");
        }

        [Fact]
        public async Task SocialLogin_MissingState_ReturnsError()
        {
            var result = await Create().ExecuteSocialLoginAsync(new SocialLoginRequest { Code = "c", State = "", Provider = "google" }, Req());

            result.Error.Should().Be("state_missing");
        }

        [Fact]
        public async Task SocialLogin_MissingProvider_ReturnsError()
        {
            var result = await Create().ExecuteSocialLoginAsync(new SocialLoginRequest { Code = "c", State = "s", Provider = "" }, Req());

            result.Error.Should().Be("provider_missing");
        }

        [Fact]
        public async Task SocialLogin_HappyPath_CallsSocialStrategy()
        {
            _strategy.Setup(s => s.AuthenticateSocialAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "social-at" });

            var result = await Create().ExecuteSocialLoginAsync(new SocialLoginRequest { Code = "c", State = "s", Provider = "google" }, Req());

            result.TokenResponse!.AccessToken.Should().Be("social-at");
            _strategy.Verify(s => s.AuthenticateSocialAsync(It.Is<TokenRequest>(t => t.GrantType == GrantTypes.Social), It.IsAny<IdentityConfiguration>()), Times.Once);
        }

        // ==================== ExecuteSwitchOrganizationAsync ====================

        [Fact]
        public async Task SwitchOrg_ConfigMissing_Returns400()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);

            var result = await Create().ExecuteSwitchOrganizationAsync(new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(), Req());

            result.Error.Should().Be(OAuthError.AuthConfigMissing);
        }

        [Fact]
        public async Task SwitchOrg_EmptyOrganizationId_ReturnsInvalidRequest()
        {
            var result = await Create().ExecuteSwitchOrganizationAsync(new SwitchOrganizationRequest { OrganizationId = "" }, Principal(), Req());

            result.Error.Should().Be("invalid_request");
        }

        [Fact]
        public async Task SwitchOrg_TenantNotResolved_Returns401()
        {
            BlocksContext.SetContext(null);

            var result = await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(tenantId: null), Req());

            result.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            result.Error.Should().Be("tenant_not_resolved");
        }

        [Fact]
        public async Task SwitchOrg_NoUserId_ReturnsInvalidUser()
        {
            var result = await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(userId: null), Req());

            result.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            result.Error.Should().Be("invalid_user");
        }

        [Fact]
        public async Task SwitchOrg_NoOrganizationAccess_ReturnsOrgNotAvailable()
        {
            _repo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(new User { ItemId = "user-1" });

            var result = await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(), Req());

            result.Error.Should().Be("organization_not_available");
        }

        [Fact]
        public async Task SwitchOrg_NoCookieToken_ReturnsSessionExpired()
        {
            _repo.Setup(r => r.GetUserByIdAsync("user-1"))
                .ReturnsAsync(new User { ItemId = "user-1", OrganizationIds = new() { "org-x" } });
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns((string)null!);

            var result = await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(), Req());

            result.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            result.Error.Should().Be(OAuthError.SessionExpired);
        }

        [Fact]
        public async Task SwitchOrg_SharedCheckRejects_ReturnsSessionExpired()
        {
            _repo.Setup(r => r.GetUserByIdAsync("user-1"))
                .ReturnsAsync(new User { ItemId = "user-1", OrganizationIds = new() { "org-x" } });
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns("rt");
            Resolves(null);

            var result = await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(), Req());

            result.Error.Should().Be(OAuthError.SessionExpired);
        }

        [Fact]
        public async Task SwitchOrg_ResolvesThroughSharedCheckRatherThanARawCacheRead()
        {
            _repo.Setup(r => r.GetUserByIdAsync("user-1"))
                .ReturnsAsync(new User { ItemId = "user-1", OrganizationIds = new() { "org-x" } });
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns("rt");
            // Deliberately no GetCacheValueAsync stub: the session is reachable only through the
            // resolver, which is what a Redis miss backed by a live store record looks like. Before
            // the fallback existed this returned session_expired.
            Resolves(Session());
            _refresher.Setup(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "switch-at" });

            var result = await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(), Req());

            result.TokenResponse!.AccessToken.Should().Be("switch-at");
            _sessionResolver.Verify(r => r.TryResolveRefreshSessionAsync("rt", It.IsAny<IdentityConfiguration>()), Times.Once);
            _refresher.Verify(r => r.GetCacheValueAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SwitchOrg_TenantOrUserMismatchInCache_ReturnsSessionExpired()
        {
            _repo.Setup(r => r.GetUserByIdAsync("user-1"))
                .ReturnsAsync(new User { ItemId = "user-1", OrganizationIds = new() { "org-x" } });
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns("rt");
            Resolves(Session(tenantId: "tenant-other"));

            var result = await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(), Req());

            result.Error.Should().Be(OAuthError.SessionExpired);
        }

        [Fact]
        public async Task SwitchOrg_GraceWindowReplay_ReturnsSessionExpiredWithoutRotating()
        {
            _repo.Setup(r => r.GetUserByIdAsync("user-1"))
                .ReturnsAsync(new User { ItemId = "user-1", OrganizationIds = new() { "org-x" } });
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns("rt");
            // The presented "rt" resolved to its successor, so the cookie is one rotation behind.
            Resolves(Session(tokenId: "rt-successor"));

            var result = await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(), Req());

            // Unlike the refresh grant, a replay must not be handed the successor here: that path
            // skips the write that binds the new organization to the token, so the switch would be
            // reverted by the next refresh. One retry is the cheaper failure.
            result.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            result.Error.Should().Be(OAuthError.SessionExpired);
            _refresher.Verify(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task SwitchOrg_HappyPath_CallsRefresher()
        {
            _repo.Setup(r => r.GetUserByIdAsync("user-1"))
                .ReturnsAsync(new User { ItemId = "user-1", OrganizationIds = new() { "org-x" } });
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns("rt");
            Resolves(Session());
            _refresher.Setup(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "switch-at" });

            var result = await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(), Req());

            result.TokenResponse!.AccessToken.Should().Be("switch-at");
            _refresher.Verify(r => r.AuthenticateAsync(It.Is<TokenRequest>(t =>
                t.GrantType == GrantTypes.SwitchOrganization
                && t.OrganizationId == "org-x"
                && t.RefreshToken == "rt"), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()), Times.Once);
        }

        // ==================== ExecuteRefreshAsync ====================

        [Fact]
        public async Task Refresh_ConfigMissing_ReturnsBadRequest()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteRefreshAsync(new RefreshRequest(), Principal(), ctx.Request, ctx.Response);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be(OAuthError.AuthConfigMissing);
        }

        [Fact]
        public async Task Refresh_NoRefreshToken_ReturnsBadRequest()
        {
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns((string)null!);
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = null }, Principal(), ctx.Request, ctx.Response);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be(OAuthError.InvalidRefreshToken);
        }

        /// <summary>
        /// Seeds the shared validity check with the session it should resolve for "rt".
        /// </summary>
        private void Resolves(RefreshTokenCache? resolved) =>
            _sessionResolver
                .Setup(r => r.TryResolveRefreshSessionAsync("rt", It.IsAny<IdentityConfiguration>()))
                .ReturnsAsync(resolved);

        private static RefreshTokenCache Session(string tokenId = "rt", string userId = "user-1", string? tenantId = TenantId) =>
            new()
            {
                RefreshToken = tokenId,
                UserId = userId,
                TenantId = tenantId,
                OrganizationId = "default",
                ClientId = "client-1",
                SessionId = "session-1",
                RefreshTokenSessionId = "lineage-1",
                IssuedUtc = DateTime.UtcNow.AddHours(-2),
                ExpiresUtc = DateTime.UtcNow.AddMinutes(30),
                AbsoluteExpiresUtc = DateTime.UtcNow.AddDays(6)
            };

        [Fact]
        public async Task Refresh_SharedCheckRejects_ReturnsBadRequestAndClearsCookies()
        {
            Resolves(null);
            _refresher.Setup(r => r.GetTenantByIDAsync(It.IsAny<string>())).ReturnsAsync((Tenant)null!);
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be(OAuthError.InvalidRefreshToken);
            _refresher.Verify(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task Refresh_SharedCheckAccepts_RotatesNormally()
        {
            Resolves(Session());
            _repo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(new User { ItemId = "user-1" });
            _refresher.Setup(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "new-at", RefreshToken = "new-rt", TokenType = "Bearer", ExpiresIn = 3600 });
            _refresher.Setup(r => r.GetTenantByIDAsync(It.IsAny<string>())).ReturnsAsync((Tenant)null!);
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            result.Should().BeOfType<OkObjectResult>();
            // Not a replay: no grace token id is carried into the rotation.
            _refresher.Verify(r => r.AuthenticateAsync(
                It.Is<TokenRequest>(t => t.GraceReplayTokenId == null), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()), Times.Once);
        }

        [Fact]
        public async Task Refresh_GraceWindowReplay_PassesSuccessorThroughWithoutRotating()
        {
            // The shared check resolved the presented token onto its rotation successor.
            var successor = Session(tokenId: "successor-rt");
            Resolves(successor);
            _repo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(new User { ItemId = "user-1" });
            _refresher.Setup(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "new-at", RefreshToken = "successor-rt", TokenType = "Bearer", ExpiresIn = 3600 });
            _refresher.Setup(r => r.GetTenantByIDAsync(It.IsAny<string>())).ReturnsAsync((Tenant)null!);
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "refresh_token").Should().Be("successor-rt");
            _refresher.Verify(r => r.AuthenticateAsync(
                It.Is<TokenRequest>(t => t.GraceReplayTokenId == "successor-rt"
                                      && t.GraceReplayAbsoluteExpiry == successor.AbsoluteExpiresUtc),
                It.IsAny<IdentityConfiguration>(), It.IsAny<User>()), Times.Once);
        }

        [Fact]
        public async Task Refresh_TokenCacheMissingUserId_ReturnsBadRequest()
        {
            Resolves(new RefreshTokenCache { RefreshToken = "rt" });
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be(OAuthError.InvalidRefreshToken);
        }

        [Fact]
        public async Task Refresh_TenantMismatch_ReturnsBadRequest()
        {
            Resolves(Session(tenantId: "tenant-other"));
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error_description").Should().Be("Refresh token tenant mismatch");
        }

        [Fact]
        public async Task Refresh_UserNotFound_ReturnsUnauthorized()
        {
            Resolves(Session());
            _repo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync((User)null!);
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            var unauth = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            Prop(unauth.Value, "error").Should().Be("invalid_user");
        }

        [Fact]
        public async Task Refresh_UserLocked_Returns423()
        {
            Resolves(Session());
            _repo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(new User { ItemId = "user-1", LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10) });
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status423Locked);
        }

        [Fact]
        public async Task Refresh_AuthenticateReturnsError_ReturnsObjectResultWithStatus()
        {
            Resolves(Session());
            _repo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(new User { ItemId = "user-1" });
            _refresher.Setup(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { Error = "invalid_grant", ErrorDescription = "nope", StatusCode = 400 });
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(400);
            Prop(obj.Value, "error").Should().Be("invalid_grant");
        }

        [Fact]
        public async Task Refresh_HappyPath_ReturnsOkWithTokens()
        {
            Resolves(Session());
            _repo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(new User { ItemId = "user-1" });
            _refresher.Setup(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "new-at", RefreshToken = "new-rt", TokenType = "Bearer", ExpiresIn = 3600 });
            _refresher.Setup(r => r.GetTenantByIDAsync(It.IsAny<string>())).ReturnsAsync((Tenant)null!);
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "access_token").Should().Be("new-at");
            Prop(ok.Value, "cookie_set").Should().Be(true);
        }

        // ==================== Impersonation pass-throughs ====================

        [Fact]
        public async Task ExecuteImpersonate_DelegatesToAuthService()
        {
            var expected = new OkObjectResult("impersonated");
            _authService.Setup(a => a.ExecuteImpersonateAsync(It.IsAny<ImpersonateRequest>(), It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>()))
                .ReturnsAsync(expected);
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteImpersonateAsync(new ImpersonateRequest { TargetTenantId = "t" }, ctx.Request, ctx.Response);

            result.Should().BeSameAs(expected);
            _authService.Verify(a => a.ExecuteImpersonateAsync(It.IsAny<ImpersonateRequest>(), It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteStopImpersonation_DelegatesToAuthService()
        {
            var expected = new OkObjectResult("stopped");
            _authService.Setup(a => a.ExecuteStopImpersonationAsync(It.IsAny<StopImpersonationRequest>(), It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>()))
                .ReturnsAsync(expected);
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteStopImpersonationAsync(new StopImpersonationRequest(), ctx.Request, ctx.Response);

            result.Should().BeSameAs(expected);
            _authService.Verify(a => a.ExecuteStopImpersonationAsync(It.IsAny<StopImpersonationRequest>(), It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>()), Times.Once);
        }
    }
}
