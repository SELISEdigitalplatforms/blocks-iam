using Authentication.DomainService.Authentication.LoginPolicies;
using XUnitTest.TestSupport;
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
using Blocks.Genesis.Entities.Iam;

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
        private readonly Mock<IUnknownUserLoginGuard> _guard = XUnitTest.TestSupport.UnknownLoginGuardStub.Create();
        private FakeLoginPolicy LoginPolicy { get; } = new();
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

            // /auth/refresh asks the resolver through ResolveRefreshSessionAsync. Routing it through the
            // TryResolveRefreshSessionAsync setups lets each test state its session once.
            _sessionResolver
                .Setup(r => r.ResolveRefreshSessionAsync(It.IsAny<string>(), It.IsAny<IdentityConfiguration>(), It.IsAny<bool>()))
                .Returns(async (string token, IdentityConfiguration configuration, bool _) =>
                    new RefreshSessionResolution(await _sessionResolver.Object.TryResolveRefreshSessionAsync(token, configuration), false));
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
                _auditWriter, NullLogger<AuthenticationFlowService>.Instance, _refreshRepo.Object, _sessionResolver.Object, _guard.Object, LoginPolicy);

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
        public async Task EmbeddedLogin_RefusedByLoginPolicy_Returns403_BeforeAnyUserLookup()
        {
            LoginPolicy.Decide = (_, _, _, _) => LoginPolicyDecision.Denied("Password sign-in is turned off for this project.");

            var result = await Create().ExecuteEmbeddedLoginAsync(new EmbeddedLoginRequest { Username = "u", Password = "p" }, Req());

            result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            result.Error.Should().Be(LoginPolicyRules.Error);
            result.ErrorDescription.Should().Be("Password sign-in is turned off for this project.");
            LoginPolicy.Checks.Should().Equal((LoginChannel.Embedded, LoginMethods.Password));
            _repo.Verify(r => r.GetUserByUsernameAsync(It.IsAny<string>()), Times.Never);
            _strategy.Verify(s => s.AuthenticatePasswordAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>()), Times.Never);
        }

        [Fact]
        public async Task EmbeddedMfaCompletion_RefusedByLoginPolicy_Returns403()
        {
            LoginPolicy.Decide = (_, _, _, _) => LoginPolicyDecision.Denied("off");

            var result = await Create().ExecuteEmbeddedLoginAsync(new EmbeddedLoginRequest { MfaId = "m", MfaCode = "123456" }, Req());

            result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            _strategy.Verify(s => s.AuthenticateMfaAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task EmbeddedSocialLogin_RefusedByLoginPolicy_Returns403_BeforeCodeExchange()
        {
            LoginPolicy.Decide = (_, method, _, _) => method == LoginMethods.Social
                ? LoginPolicyDecision.Denied("Social sign-in is turned off for this project.")
                : LoginPolicyDecision.Allowed;

            var result = await Create().ExecuteSocialLoginAsync(new SocialLoginRequest { Code = "c", State = "s", Provider = "google" }, Req());

            result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            result.Error.Should().Be(LoginPolicyRules.Error);
            LoginPolicy.Checks.Should().Equal((LoginChannel.Embedded, LoginMethods.Social));
            _strategy.Verify(s => s.AuthenticateSocialAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>()), Times.Never);
        }

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
                .ReturnsAsync(new User { ItemId = "user-1", Active = true, IsVerified = true, LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10) });

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
                .ReturnsAsync(new User { ItemId = "user-1", Active = true, IsVerified = true, FailedLoginCount = CaptchaGate.FailedAttemptsBeforeCaptcha });
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
                .ReturnsAsync(new User { ItemId = "user-1", Active = true, IsVerified = true, FailedLoginCount = CaptchaGate.FailedAttemptsBeforeCaptcha });
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
            // C6: rejected before the refresh token is even read, so nothing rotates and no cookie changes.
            VerifyNoRotation();
            _sessionResolver.Verify(r => r.TryResolveRefreshSessionAsync(It.IsAny<string>(), It.IsAny<IdentityConfiguration>()), Times.Never);
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
            VerifyNoRotation();
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
            VerifyNoRotation();
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
        public async Task SwitchOrg_DeactivatedUser_ReturnsSessionExpired_RevokesTheLineage_AndDoesNotRotate()
        {
            _repo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(new User
            {
                ItemId = "user-1", OrganizationIds = new() { "org-x" },
                Active = false, IsVerified = true, Status = UserLifecycleStatus.Disabled, DeactivatedAtUtc = DateTime.UtcNow
            });
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns("rt");
            Resolves(Session());

            var result = await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(), Req());

            result.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            result.Error.Should().Be(OAuthError.SessionExpired);
            _refreshRepo.Verify(r => r.RevokeLineageAndEvictAsync("lineage-1", BlockedUserRefreshGuard.RevokeReason), Times.Once);
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

        [Fact]
        public async Task SwitchOrg_HappyPath_PersistsLastUsedOrganization()
        {
            // Without this the switch was invisible to the next sign-in: password, social and mfa
            // logins all resolve the organization from LastUsedOrganizationId, so a user who
            // switched to org-x and logged back in silently landed on their previous organization.
            _repo.Setup(r => r.GetUserByIdAsync("user-1"))
                .ReturnsAsync(new User { ItemId = "user-1", LastUsedOrganizationId = "org-old", OrganizationIds = new() { "org-old", "org-x" } });
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns("rt");
            Resolves(Session());
            _refresher.Setup(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "switch-at" });

            await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(), Req());

            _repo.Verify(r => r.UpdatePartialAsync<User>("user-1",
                It.Is<Dictionary<string, object>>(d =>
                    d.ContainsKey(nameof(User.LastUsedOrganizationId))
                    && (string)d[nameof(User.LastUsedOrganizationId)] == "org-x"),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task SwitchOrg_AlreadyOnThatOrganization_SkipsTheWrite()
        {
            _repo.Setup(r => r.GetUserByIdAsync("user-1"))
                .ReturnsAsync(new User { ItemId = "user-1", LastUsedOrganizationId = "org-x", OrganizationIds = new() { "org-x" } });
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns("rt");
            Resolves(Session());
            _refresher.Setup(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "switch-at" });

            await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(), Req());

            _repo.Verify(r => r.UpdatePartialAsync<User>(It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SwitchOrg_TokenIssuanceFailed_DoesNotPersistLastUsedOrganization()
        {
            _repo.Setup(r => r.GetUserByIdAsync("user-1"))
                .ReturnsAsync(new User { ItemId = "user-1", OrganizationIds = new() { "org-x" } });
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns("rt");
            Resolves(Session());
            _refresher.Setup(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { Error = OAuthError.InvalidRefreshToken, StatusCode = 400 });

            await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(), Req());

            _repo.Verify(r => r.UpdatePartialAsync<User>(It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SwitchOrg_StickinessWriteThrows_StillReturnsTheIssuedTokens()
        {
            // The switch has already succeeded by this point. A failed convenience write must not
            // discard tokens the caller is entitled to.
            _repo.Setup(r => r.GetUserByIdAsync("user-1"))
                .ReturnsAsync(new User { ItemId = "user-1", OrganizationIds = new() { "org-x" } });
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns("rt");
            Resolves(Session());
            _refresher.Setup(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "switch-at" });
            _repo.Setup(r => r.UpdatePartialAsync<User>(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("mongo down"));

            var result = await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(), Req());

            result.TokenResponse!.AccessToken.Should().Be("switch-at");
        }

        // ==================== blocks-iam#624 — switch-org keeps the lineage's OIDC client ====================

        private void VerifyNoRotation() =>
            _refresher.Verify(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()), Times.Never);

        private List<TokenRequest> CaptureRotations(TokenResponse response)
        {
            var captured = new List<TokenRequest>();
            _refresher.Setup(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .Callback<TokenRequest, IdentityConfiguration, User>((t, _, _) => captured.Add(t))
                .ReturnsAsync(response);
            return captured;
        }

        [Fact]
        public async Task SwitchOrg_HappyPath_CarriesTheLineageClientIntoTheRotation()
        {
            // H1/H2/H6/H8: the rotation request names the lineage client and the requested organization;
            // lineage and cap are inherited downstream from the same predecessor (RefreshTokenLineageTests).
            _repo.Setup(r => r.GetUserByIdAsync("user-1"))
                .ReturnsAsync(new User { ItemId = "user-1", OrganizationIds = new() { "org-x" } });
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns("rt");
            Resolves(Session());
            var rotations = CaptureRotations(new TokenResponse { AccessToken = "switch-at" });

            var result = await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(), Req());

            result.TokenResponse!.AccessToken.Should().Be("switch-at");
            var request = rotations.Should().ContainSingle().Subject;
            request.ClientId.Should().Be("client-1");
            request.OrganizationId.Should().Be("org-x");
            request.GrantType.Should().Be(GrantTypes.SwitchOrganization);
            request.RefreshToken.Should().Be("rt");
        }

        [Fact]
        public async Task SwitchOrg_LineageWithAStaleAudience_DoesNotCarryTheAudienceIntoTheRotation()
        {
            // C12: aud comes from the tenant at mint time; the rotation request must not re-narrow it.
            _repo.Setup(r => r.GetUserByIdAsync("user-1"))
                .ReturnsAsync(new User { ItemId = "user-1", OrganizationIds = new() { "org-x" } });
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns("rt");
            var session = Session();
            session.Audience = "stale-client-audience";
            Resolves(session);
            var rotations = CaptureRotations(new TokenResponse { AccessToken = "switch-at" });

            await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(), Req());

            rotations.Should().ContainSingle().Which.Audience.Should().BeNull();
        }

        [Fact]
        public async Task SwitchOrg_ClientLessLineage_RotatesWithoutInventingAClient()
        {
            // Out of scope to repair: an already client-less lineage passes no client.
            _repo.Setup(r => r.GetUserByIdAsync("user-1"))
                .ReturnsAsync(new User { ItemId = "user-1", OrganizationIds = new() { "org-x" } });
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns("rt");
            var session = Session();
            session.ClientId = string.Empty;
            Resolves(session);
            var rotations = CaptureRotations(new TokenResponse { AccessToken = "switch-at" });

            await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(), Req());

            rotations.Should().ContainSingle().Which.ClientId.Should().BeEmpty();
        }

        [Fact]
        public async Task SwitchOrg_ForeignUserInSession_ReturnsSessionExpiredWithoutRotating()
        {
            // C8 (user half): a refresh token belonging to someone else is rejected before any rotation.
            _repo.Setup(r => r.GetUserByIdAsync("user-1"))
                .ReturnsAsync(new User { ItemId = "user-1", OrganizationIds = new() { "org-x" } });
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns("rt");
            Resolves(Session(userId: "user-other"));

            var result = await Create().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = "org-x" }, Principal(), Req());

            result.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            result.Error.Should().Be(OAuthError.SessionExpired);
            VerifyNoRotation();
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

        [Theory]
        // body refresh_token, cookie, body refreshToken → the token that is resolved
        [InlineData("body", "cookie", "client", "body")]
        [InlineData(null, "cookie", "client", "cookie")]
        [InlineData(null, null, "client", "client")]
        [InlineData("body", null, null, "body")]
        public async Task Refresh_PicksTokenInOrder_BodyThenCookieThenClientField(
            string? body, string? cookie, string? client, string expected)
        {
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns(cookie!);
            _sessionResolver
                .Setup(r => r.TryResolveRefreshSessionAsync(It.IsAny<string>(), It.IsAny<IdentityConfiguration>()))
                .ReturnsAsync((RefreshTokenCache?)null);
            _refresher.Setup(r => r.GetTenantByIDAsync(It.IsAny<string>())).ReturnsAsync((Tenant)null!);
            var ctx = new DefaultHttpContext();

            await Create().ExecuteRefreshAsync(
                new RefreshRequest { RefreshToken = body, ClientRefreshToken = client }, Principal(), ctx.Request, ctx.Response);

            _sessionResolver.Verify(r => r.TryResolveRefreshSessionAsync(expected, It.IsAny<IdentityConfiguration>()), Times.Once);
        }

        [Fact]
        public void RefreshRequest_BindsBothBodyNames()
        {
            var snake = System.Text.Json.JsonSerializer.Deserialize<RefreshRequest>("{\"refresh_token\":\"a\"}")!;
            var camel = System.Text.Json.JsonSerializer.Deserialize<RefreshRequest>("{\"refreshToken\":\"b\"}")!;

            snake.RefreshToken.Should().Be("a");
            snake.ClientRefreshToken.Should().BeNull();
            camel.ClientRefreshToken.Should().Be("b");
            camel.RefreshToken.Should().BeNull();
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
        public async Task Refresh_TokenReplacedByAnImpersonationChange_SaysSoAndLeavesTheCookiesAlone()
        {
            // The cookie may already hold the token the impersonation change issued -- set by another tab --
            // so clearing it would sign that tab out too.
            _sessionResolver
                .Setup(r => r.ResolveRefreshSessionAsync("rt", It.IsAny<IdentityConfiguration>(), false))
                .ReturnsAsync(RefreshSessionResolution.Changed);
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be(OAuthError.InvalidRefreshToken);
            Prop(bad.Value, "error_description").Should().Be(OAuthError.SessionChanged);
            // ClearSessionCookies resolves the tenant's cookie domain first; it never got that far.
            _refresher.Verify(r => r.GetTenantByIDAsync(It.IsAny<string>()), Times.Never);
            _refresher.Verify(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task Refresh_ImpersonatedSession_IsRejectedBeforeRotating()
        {
            // Nothing below this point carries IsImpersonation, so rotating here would hand the browser
            // root-scoped cookies for a session the UI still shows as a project -- behind an HTTP 200.
            var impersonated = Session();
            impersonated.Impersonated = true;
            impersonated.ImpersonationId = "imp-1";
            Resolves(impersonated);
            _repo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(new User { ItemId = "user-1" });
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
        public async Task Refresh_DeactivatedUser_IsRefused_RevokesTheLineage_AndDoesNotRotate()
        {
            Resolves(Session());
            _repo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(new User
            {
                ItemId = "user-1", Active = false, IsVerified = true,
                Status = UserLifecycleStatus.Disabled, DeactivatedAtUtc = DateTime.UtcNow
            });
            _refresher.Setup(r => r.GetTenantByIDAsync(It.IsAny<string>())).ReturnsAsync((Tenant)null!);
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be(OAuthError.InvalidRefreshToken);
            Prop(bad.Value, "error_description").Should().Be("Refresh token is invalid or expired");
            _refreshRepo.Verify(r => r.RevokeLineageAndEvictAsync("lineage-1", BlockedUserRefreshGuard.RevokeReason), Times.Once);
            _refresher.Verify(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task Refresh_ActiveUser_RotatesAndRevokesNothing()
        {
            Resolves(Session());
            _repo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(new User
            {
                ItemId = "user-1", Active = true, IsVerified = true, Status = UserLifecycleStatus.Active
            });
            _refresher.Setup(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "new-at", RefreshToken = "new-rt", TokenType = "Bearer", ExpiresIn = 3600 });
            _refresher.Setup(r => r.GetTenantByIDAsync(It.IsAny<string>())).ReturnsAsync((Tenant)null!);
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            result.Should().BeOfType<OkObjectResult>();
            _refreshRepo.Verify(r => r.RevokeLineageAndEvictAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Theory] // The lineage's organization is only a hint; a blank one must not become "default"
        [InlineData("org-a", "org-a")]
        [InlineData("default", "default")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public async Task Refresh_PassesTheLineageOrganizationThrough_WithoutCollapsingBlankToDefault(string? stored, string expected)
        {
            var session = Session();
            session.OrganizationId = stored;
            Resolves(session);
            _repo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(new User { ItemId = "user-1" });
            _refresher.Setup(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "new-at", RefreshToken = "new-rt", TokenType = "Bearer", ExpiresIn = 3600 });
            _refresher.Setup(r => r.GetTenantByIDAsync(It.IsAny<string>())).ReturnsAsync((Tenant)null!);
            var ctx = new DefaultHttpContext();

            await Create().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            _refresher.Verify(r => r.AuthenticateAsync(
                It.Is<TokenRequest>(t => t.OrganizationId == expected),
                It.IsAny<IdentityConfiguration>(), It.IsAny<User>()), Times.Once);
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
            ok.Value.Should().BeAssignableTo<IDictionary<string, object?>>()
                .Which["refresh_token"].Should().Be("successor-rt");
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
            VerifyNoRotation();
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
        public async Task Refresh_UserLocked_StillRefreshes_SoALockCannotSignSomeoneOut()
        {
            Resolves(Session());
            var locked = new User { ItemId = "user-1", Active = true, IsVerified = true, LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10) };
            _repo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(locked);
            _refresher.Setup(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse { Error = "invalid_grant", StatusCode = 400 });
            var ctx = new DefaultHttpContext();

            await Create().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            _refresher.Verify(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), locked), Times.Once);
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
        public async Task Refresh_NoAppDomain_ReturnsTokensInBody()
        {
            var body = await RefreshOkBody(tenant: null, origin: null);

            body["access_token"].Should().Be("new-at");
            body["refresh_token"].Should().Be("new-rt");
            body["cookie_set"].Should().Be(false);
        }

        [Fact]
        public async Task Refresh_CookiesSet_OmitsTokensFromBody()
        {
            var ctx = new DefaultHttpContext();

            var body = await RefreshOkBody(AppTenant(), "https://app.example.com", ctx);

            body["cookie_set"].Should().Be(true);
            body.Should().NotContainKey("access_token");
            body.Should().NotContainKey("refresh_token");
            ctx.Response.Headers.Should().ContainKey("Set-Cookie");
        }

        [Fact]
        public async Task Refresh_ClientOptedOutOfCookies_ReturnsTokensInBodyAndSetsNoCookie()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("client-1"))
                .ReturnsAsync(new OidcClientRegistration { ClientId = "client-1", UseTokensCookie = false });
            var ctx = new DefaultHttpContext();

            var body = await RefreshOkBody(AppTenant(), "https://app.example.com", ctx);

            body["access_token"].Should().Be("new-at");
            body["cookie_set"].Should().Be(false);
            ctx.Response.Headers.Should().NotContainKey("Set-Cookie");
        }

        private async Task<IDictionary<string, object?>> RefreshOkBody(Tenant? tenant, string? origin, DefaultHttpContext? ctx = null)
        {
            Resolves(Session());
            _repo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(new User { ItemId = "user-1" });
            _refresher.Setup(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .ReturnsAsync(new TokenResponse
                {
                    AccessToken = "new-at", RefreshToken = "new-rt", TokenType = "Bearer", ExpiresIn = 3600,
                    ExpiresUtc = DateTime.UtcNow.AddHours(1), RefreshExpiresUtc = DateTime.UtcNow.AddHours(5)
                });
            _refresher.Setup(r => r.GetTenantByIDAsync(It.IsAny<string>())).ReturnsAsync(tenant!);
            ctx ??= new DefaultHttpContext();
            if (origin != null)
            {
                ctx.Request.Headers["Origin"] = origin;
            }

            var result = await Create().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            return ok.Value.Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
        }

        private static Tenant AppTenant() => new()
        {
            TenantId = TenantId,
            DbConnectionString = "",
            Applications = new List<Applications> { new() { Domain = "app.example.com", CookieDomain = ".example.com", IsDomainVerified = true } },
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "", IssueDate = DateTime.UtcNow }
        };

        // ==================== blocks-iam#624 — /auth/refresh keeps the lineage's OIDC client ====================

        [Fact]
        public async Task Refresh_HostedLineage_CarriesTheLineageClientAndNoAudienceIntoTheRotation()
        {
            // H4 + C12: the client is carried; the stored audience is not.
            var session = Session();
            session.Audience = "stale-client-audience";
            Resolves(session);
            // The browser shape: an empty body, the refresh token in the rt_ cookie.
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns("rt");
            _repo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(new User { ItemId = "user-1" });
            var rotations = CaptureRotations(new TokenResponse { AccessToken = "new-at", RefreshToken = "new-rt", TokenType = "Bearer", ExpiresIn = 3600 });
            _refresher.Setup(r => r.GetTenantByIDAsync(It.IsAny<string>())).ReturnsAsync((Tenant)null!);
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteRefreshAsync(new RefreshRequest(), Principal(), ctx.Request, ctx.Response);

            result.Should().BeOfType<OkObjectResult>();
            var request = rotations.Should().ContainSingle().Subject;
            request.ClientId.Should().Be("client-1");
            request.Audience.Should().BeNull();
            request.GrantType.Should().Be(GrantTypes.RefreshToken);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task Refresh_ClientLessLineage_StillRotatesWith200(string? lineageClient)
        {
            // C3: embedded /auth/login lineages (and ones broken before the fix) keep refreshing here.
            var session = Session();
            session.ClientId = lineageClient;
            Resolves(session);
            _repo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(new User { ItemId = "user-1" });
            var rotations = CaptureRotations(new TokenResponse { AccessToken = "new-at", RefreshToken = "new-rt", TokenType = "Bearer", ExpiresIn = 3600 });
            _refresher.Setup(r => r.GetTenantByIDAsync(It.IsAny<string>())).ReturnsAsync((Tenant)null!);
            var ctx = new DefaultHttpContext();

            var result = await Create().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            result.Should().BeOfType<OkObjectResult>();
            rotations.Should().ContainSingle().Which.ClientId.Should().BeNullOrEmpty();
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
