using System.Security.Claims;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XUnitTest.TestDoubles;
using XUnitTest.TestSupport;

namespace XUnitTest.Auth.DisabledOrganizations
{
    /// <summary>
    /// Spec #668 at OIDC authorize: C5 (redirect mode to the IAM login error URL, 403/503 JSON otherwise),
    /// C3 (no code, last-used unchanged), C4, H3, H4 (org_status on the code and the body), C1 (link session)
    /// and C12/Q6 (impersonated authorize not gated).
    /// </summary>
    public sealed class OidcAuthorizationEndpointOrganizationGateTests : IDisposable
    {
        private const string RedirectUri = "https://app.example.com/callback";
        private const string A = "org-a";
        private const string D = "org-d";

        private readonly Mock<IAuthorizationCodeRepository> _authCodeRepo = new();
        private readonly Mock<IIdpSessionRepository> _sessionRepo = new();
        private readonly Mock<IIdpSessionService> _sessionService = new();
        private readonly Mock<IPkceService> _pkce = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<Authentication.DomainService.Authentication.IAuthenticationService> _authService = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IResourceRepository> _resourceRepo = new();
        private readonly FakeLoginPolicy _loginPolicy = new();
        private readonly Mock<ILinkSessionRepository> _linkSessions = new();
        private readonly OidcAuthorizationEndpoint _endpoint;

        public OidcAuthorizationEndpointOrganizationGateTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));

            _pkce.Setup(p => p.GenerateRandomCode(It.IsAny<int>())).Returns("authcode123");
            _sessionRepo.Setup(s => s.CreateAsync(It.IsAny<IdpSessionModel>())).ReturnsAsync("new-sess");
            _sessionRepo.Setup(s => s.UpdateActivityAsync(It.IsAny<string>())).ReturnsAsync(true);
            _sessionRepo.Setup(s => s.AddAccountAsync(It.IsAny<string>(), It.IsAny<IdpSessionAccount>())).ReturnsAsync(true);
            _authCodeRepo.Setup(a => a.CreateAsync(It.IsAny<AuthorizationCodeModel>())).ReturnsAsync("code-id");
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync((string)null!);
            _userRepo.Setup(u => u.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _sessionService.Setup(s => s.RotateSessionAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync("rotated-sess");
            _authRepo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>()))
                .ReturnsAsync(new OidcClientRegistration { ClientId = "client-1", RedirectUris = [RedirectUri], RequirePkce = false });
            _resourceRepo.WithOrganizationStatuses(disabled: [D]);
            MultiOrg(allowNoOrg: false);

            _endpoint = new OidcAuthorizationEndpoint(
                new OidcAuthorizationSessionStores(_authCodeRepo.Object, _sessionRepo.Object, _sessionService.Object, _pkce.Object, _linkSessions.Object),
                new OidcAuthorizationIdentityStores(_userRepo.Object, _authRepo.Object, _authService.Object, _tenants.Object, _cache.Object, _resourceRepo.Object, _loginPolicy),
                NullLogger<OidcAuthorizationEndpoint>.Instance);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private void MultiOrg(bool allowNoOrg) =>
            _resourceRepo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new IamTenantConfiguration { IsMultiOrgEnabled = true, AllowNoOrgSignInForDisabledOrganizations = allowNoOrg });

        private void UserIs(User user) => _userRepo.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(user);

        private static User MakeUser(string id = "user-1", string? lastUsed = null, params string[] organizationIds) => new()
        {
            ItemId = id,
            Email = "u@example.com",
            Active = true,
            IsVerified = true,
            UserMfaType = UserMfaType.Email,
            OrganizationIds = [.. organizationIds],
            LastUsedOrganizationId = lastUsed
        };

        private Task<IActionResult> Authorize(bool returnRedirectResponse, HttpContext? ctx = null, string? tenantId = null, string? blocksUserId = "user-1")
        {
            ctx ??= new DefaultHttpContext();
            return _endpoint.AuthorizeAsync(
                "client-1", "code", RedirectUri, "openid profile", "st", "nonce-1", "", "", null, tenantId,
                ctx.Request, ctx.Response, blocksUserId, returnRedirectResponse, false, null);
        }

        private static object? Prop(object? value, string name) => value?.GetType().GetProperty(name)?.GetValue(value);

        private void VerifyNoCodeAndNoLastUsed()
        {
            _authCodeRepo.Verify(a => a.CreateAsync(It.IsAny<AuthorizationCodeModel>()), Times.Never);
            _authRepo.Verify(r => r.UpdatePartialAsync<User>(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()), Times.Never);
            _userRepo.Verify(u => u.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task C5_RedirectMode_GoesToTheIamLoginErrorUrl_NeverTheClient()
        {
            UserIs(MakeUser(lastUsed: D, organizationIds: D));

            var result = await Authorize(returnRedirectResponse: true);

            var redirect = result.Should().BeOfType<RedirectResult>().Subject;
            redirect.Url.Should().StartWith("/oidc/login?");
            redirect.Url.Should().NotStartWith(RedirectUri);
            redirect.Url.Should().Contain("error=organization_disabled");
            redirect.Url.Should().NotContain("code=");
            VerifyNoCodeAndNoLastUsed();
        }

        [Fact]
        public async Task C5_NonRedirectMode_403Json()
        {
            UserIs(MakeUser(lastUsed: D, organizationIds: D));

            var result = await Authorize(returnRedirectResponse: false);

            var refusal = result.Should().BeOfType<OrganizationRefusalResult>().Subject;
            refusal.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            Prop(refusal.Value, "error").Should().Be("organization_disabled");
            Prop(refusal.Value, "error_description").Should().Be("Your organization is disabled. Please contact your administrator or support.");
            VerifyNoCodeAndNoLastUsed();
        }

        [Fact]
        public async Task C4_StatusReadThrows_503_NonRedirect_AndErrorUrlInRedirectMode()
        {
            _resourceRepo.Setup(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>())).ThrowsAsync(new TimeoutException());
            UserIs(MakeUser(lastUsed: A, organizationIds: A));

            var json = await Authorize(returnRedirectResponse: false);
            var redirect = await Authorize(returnRedirectResponse: true);

            var refusal = json.Should().BeOfType<OrganizationRefusalResult>().Subject;
            refusal.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
            Prop(refusal.Value, "error").Should().Be("temporarily_unavailable");
            redirect.Should().BeOfType<RedirectResult>().Subject.Url.Should().StartWith("/oidc/login?").And.Contain("error=temporarily_unavailable");
            VerifyNoCodeAndNoLastUsed();
        }

        [Fact]
        public async Task H3_MixedMemberships_StoresTheActiveOrganization()
        {
            UserIs(MakeUser(lastUsed: D, organizationIds: [D, A]));

            var result = await Authorize(returnRedirectResponse: true);

            result.Should().BeOfType<RedirectResult>().Subject.Url.Should().StartWith(RedirectUri).And.Contain("code=authcode123");
            _authCodeRepo.Verify(a => a.CreateAsync(It.Is<AuthorizationCodeModel>(m => m.OrganizationId == A && m.OrganizationStatus == null)), Times.Once);
            _authRepo.Verify(r => r.UpdatePartialAsync<User>("user-1",
                It.Is<Dictionary<string, object>>(d => (string)d[nameof(User.LastUsedOrganizationId)] == A), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task H4_AllDisabled_SettingTrue_OrgStatusOnTheCodeAndTheOkBody()
        {
            MultiOrg(allowNoOrg: true);
            UserIs(MakeUser(lastUsed: D, organizationIds: D));

            var result = await Authorize(returnRedirectResponse: false);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "org_status").Should().Be("disabled");
            ((string)Prop(ok.Value, "redirect_uri")!).Should().StartWith(RedirectUri);
            _authCodeRepo.Verify(a => a.CreateAsync(It.Is<AuthorizationCodeModel>(m =>
                m.OrganizationId == IdpConstants.NoOrganizationId && m.OrganizationStatus == "disabled")), Times.Once);
            // "no-org" is never written as last-used (§6.3).
            _authRepo.Verify(r => r.UpdatePartialAsync<User>(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ActiveOrganization_OkBody_HasNoOrgStatus()
        {
            UserIs(MakeUser(lastUsed: A, organizationIds: A));

            var result = await Authorize(returnRedirectResponse: false);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value!.GetType().GetProperty("org_status").Should().BeNull();
        }

        [Fact]
        public async Task C1_LinkSessionOrganizationDisabled_Refused()
        {
            UserIs(MakeUser(id: "link-user", lastUsed: A, organizationIds: [D, A]));
            _linkSessions.Setup(l => l.GetBySessionIdAsync("t1", "LS")).ReturnsAsync(new LinkSessionModel
            {
                SessionId = "LS",
                UserId = "link-user",
                TenantId = "t1",
                ClientId = "client-1",
                OrganizationId = D,
                Roles = ["r1"],
                Amr = ["link"],
                IssuedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(20)
            });
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers["Cookie"] = "blocks-link-session-t1=LS";

            var result = await Authorize(returnRedirectResponse: false, ctx: ctx, tenantId: "t1", blocksUserId: null);

            result.Should().BeOfType<OrganizationRefusalResult>().Which.StatusCode.Should().Be(403);
            VerifyNoCodeAndNoLastUsed();
        }

        [Fact]
        public async Task C12_ImpersonatedAuthorize_IsNotGated()
        {
            UserIs(MakeUser(lastUsed: D, organizationIds: D));
            _authService.Setup(s => s.GetPrincipalFromTokenAsync(It.IsAny<HttpRequest>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("impersonated", "true"),
                    new Claim("sub", "imp-user"),
                    new Claim("tenant_id", "ten-x")
                })));

            var result = await Authorize(returnRedirectResponse: true);

            result.Should().BeOfType<RedirectResult>().Subject.Url.Should().StartWith(RedirectUri);
            _authCodeRepo.Verify(a => a.CreateAsync(It.Is<AuthorizationCodeModel>(m => m.Impersonated && m.OrganizationId == D)), Times.Once);
            _resourceRepo.Verify(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
        }
    }
}
