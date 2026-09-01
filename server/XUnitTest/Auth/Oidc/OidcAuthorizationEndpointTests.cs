using System.Security.Claims;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Iam.DomainService.Resources;
using Iam.DomainService.Shared.Entities;

namespace XUnitTest.Auth.Oidc
{
    /// <summary>
    /// Branch coverage for <see cref="OidcAuthorizationEndpoint.AuthorizeAsync"/>: request validation,
    /// session resolution, lockout gates, client / redirect_uri checks, error redirects, code issuance
    /// (redirect and JSON variants), AMR / impersonation enrichment and the server_error paths.
    /// </summary>
    public class OidcAuthorizationEndpointTests : IDisposable
    {
        private readonly Mock<IAuthorizationCodeRepository> _authCodeRepo = new();
        private readonly Mock<IIdpSessionRepository> _sessionRepo = new();
        private readonly Mock<IPkceService> _pkce = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<Authentication.DomainService.Authentication.IAuthenticationService> _authService = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IResourceRepository> _resourceRepo = new();

        private readonly OidcAuthorizationEndpoint _endpoint;

        private const string RedirectUri = "https://app.example.com/callback";

        public OidcAuthorizationEndpointTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));

            // Benign defaults so deep paths do not blow up on un-stubbed members.
            _pkce.Setup(p => p.GenerateRandomCode(It.IsAny<int>())).Returns("authcode123");
            _sessionRepo.Setup(s => s.CreateAsync(It.IsAny<IdpSessionModel>())).ReturnsAsync("new-sess");
            _sessionRepo.Setup(s => s.UpdateActivityAsync(It.IsAny<string>())).ReturnsAsync(true);
            _sessionRepo.Setup(s => s.AddAccountAsync(It.IsAny<string>(), It.IsAny<IdpSessionAccount>())).ReturnsAsync(true);
            _authCodeRepo.Setup(a => a.CreateAsync(It.IsAny<AuthorizationCodeModel>())).ReturnsAsync("code-id");
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync((string)null!);
            _userRepo.Setup(u => u.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

            _endpoint = new OidcAuthorizationEndpoint(
                _authCodeRepo.Object,
                _sessionRepo.Object,
                _pkce.Object,
                _userRepo.Object,
                _authRepo.Object,
                _authService.Object,
                _tenants.Object,
                _cache.Object,
                _resourceRepo.Object,
                NullLogger<OidcAuthorizationEndpoint>.Instance);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        // ---------- helpers ----------

        private static object? Prop(object? value, string name) =>
            value?.GetType().GetProperty(name)?.GetValue(value);

        private static HttpContext Ctx(string? cookie = null)
        {
            var ctx = new DefaultHttpContext();
            if (!string.IsNullOrEmpty(cookie))
            {
                ctx.Request.Headers["Cookie"] = cookie;
            }
            return ctx;
        }

        private Task<IActionResult> Authorize(
            string client_id = "client-1",
            string response_type = "code",
            string redirect_uri = RedirectUri,
            string scope = "openid profile",
            string state = "st",
            string nonce = "nonce-1",
            string code_challenge = "",
            string code_challenge_method = "",
            string? prompt = null,
            string? tenant_id = null,
            HttpContext? ctx = null,
            string? blocksUserId = null,
            bool returnRedirectResponse = true,
            bool mfaCompleted = false)
        {
            ctx ??= new DefaultHttpContext();
            return _endpoint.AuthorizeAsync(
                client_id, response_type, redirect_uri, scope, state, nonce,
                code_challenge, code_challenge_method, prompt, tenant_id,
                ctx.Request, ctx.Response, blocksUserId, returnRedirectResponse, mfaCompleted);
        }

        private void ClientExists(string redirectUri = RedirectUri) =>
            _authRepo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>()))
                .ReturnsAsync(new OidcClientRegistration
                {
                    ClientId = "client-1",
                    RedirectUris = new List<string> { redirectUri }
                });

        private static User ValidUser(string id = "user-1") => new()
        {
            ItemId = id,
            Email = "u@example.com",
            Active = true,
            IsVerified = true,
            UserMfaType = UserMfaType.Email
        };

        private static IdpSessionModel Session(string sessionId, params IdpSessionAccount[] accounts) => new()
        {
            SessionId = sessionId,
            Accounts = accounts.ToList(),
            RevokedAt = null,
            IdleExpiry = DateTime.UtcNow.AddHours(1),
            AbsoluteExpiry = DateTime.UtcNow.AddHours(5)
        };

        // ================= validation branches =================

        [Fact]
        public async Task AuthorizeAsync_ReturnsInvalidRequest_WhenAllRequiredParamsMissing()
        {
            var result = await Authorize(client_id: "", response_type: "", redirect_uri: "", scope: "",
                returnRedirectResponse: false);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_request");
            var desc = Prop(bad.Value, "error_description") as string;
            desc.Should().Contain("client_id is required");
            desc.Should().Contain("response_type is required");
            desc.Should().Contain("redirect_uri is required");
            desc.Should().Contain("scope is required");
        }

        [Fact]
        public async Task AuthorizeAsync_ReturnsInvalidRequest_WhenResponseTypeNotCode()
        {
            var result = await Authorize(response_type: "token", returnRedirectResponse: false);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            (Prop(bad.Value, "error_description") as string).Should().Contain("response_type must be 'code'");
        }

        [Fact]
        public async Task AuthorizeAsync_ReturnsInvalidRequest_WhenScopeMissingOpenid()
        {
            var result = await Authorize(scope: "profile email", returnRedirectResponse: false);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            (Prop(bad.Value, "error_description") as string).Should().Contain("scope must include 'openid'");
        }

        [Fact]
        public async Task AuthorizeAsync_ReturnsInvalidRequest_WhenCodeChallengeFormatInvalid()
        {
            var result = await Authorize(code_challenge: "too-short", code_challenge_method: "S256",
                returnRedirectResponse: false);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            (Prop(bad.Value, "error_description") as string).Should().Contain("code_challenge has invalid format");
        }

        [Fact]
        public async Task AuthorizeAsync_ReturnsInvalidRequest_WhenCodeChallengeMethodMissing()
        {
            var challenge = new string('a', 43); // valid BASE64URL length, method omitted
            var result = await Authorize(code_challenge: challenge, code_challenge_method: "",
                returnRedirectResponse: false);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            (Prop(bad.Value, "error_description") as string)
                .Should().Contain("code_challenge_method is required when code_challenge is provided");
        }

        [Fact]
        public async Task AuthorizeAsync_ReturnsInvalidRequest_WhenCodeChallengeMethodNotS256()
        {
            var result = await Authorize(code_challenge: "", code_challenge_method: "plain",
                returnRedirectResponse: false);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            (Prop(bad.Value, "error_description") as string).Should().Contain("plain method not supported");
        }

        [Fact]
        public async Task AuthorizeAsync_RedirectsWithError_WhenValidationFails_AndRedirectUriPresent()
        {
            var result = await Authorize(scope: "profile", returnRedirectResponse: true);

            var redirect = result.Should().BeOfType<RedirectResult>().Subject;
            redirect.Url.Should().StartWith(RedirectUri);
            redirect.Url.Should().Contain("error=invalid_request");
            redirect.Url.Should().Contain("state=st");
        }

        // ================= session / auth resolution =================

        [Fact]
        public async Task AuthorizeAsync_RedirectsToLogin_WhenNoSessionAndNoUser()
        {
            var result = await Authorize();

            var redirect = result.Should().BeOfType<RedirectResult>().Subject;
            redirect.Url.Should().StartWith("/oidc/login?");
            redirect.Url.Should().Contain("client_id=client-1");
            redirect.Url.Should().Contain("redirect_uri=");
        }

        [Fact]
        public async Task AuthorizeAsync_ResolvesUserFromSessionCookie_AndIssuesCode()
        {
            ClientExists();
            _userRepo.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(ValidUser());
            _sessionRepo.Setup(s => s.GetBySessionIdAsync("SID"))
                .ReturnsAsync(Session("SID", new IdpSessionAccount { UserId = "user-1", TenantId = "" }));

            var ctx = Ctx("idp_session_id=SID");
            var result = await Authorize(ctx: ctx);

            result.Should().BeOfType<RedirectResult>();
            _sessionRepo.Verify(s => s.UpdateActivityAsync("SID"), Times.AtLeastOnce);
            _authCodeRepo.Verify(a => a.CreateAsync(It.Is<AuthorizationCodeModel>(m => m.UserId == "user-1")), Times.Once);
        }

        [Fact]
        public async Task AuthorizeAsync_AddsAccountToSession_WhenUserNotInSession()
        {
            ClientExists();
            _userRepo.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(ValidUser("user-3"));
            // Two accounts => resolution does not collapse to a single user, so blocksUserId survives.
            _sessionRepo.Setup(s => s.GetBySessionIdAsync("SID"))
                .ReturnsAsync(Session("SID",
                    new IdpSessionAccount { UserId = "user-1", TenantId = "tenant-a" },
                    new IdpSessionAccount { UserId = "user-2", TenantId = "tenant-b" }));

            var ctx = Ctx("idp_session_id=SID");
            var result = await Authorize(ctx: ctx, blocksUserId: "user-3");

            result.Should().BeOfType<RedirectResult>();
            _sessionRepo.Verify(s => s.AddAccountAsync("SID", It.Is<IdpSessionAccount>(a => a.UserId == "user-3")), Times.Once);
        }

        // ================= lockout gates =================

        [Fact]
        public async Task AuthorizeAsync_ReturnsAccountLocked_WhenUserLockedOut()
        {
            var locked = ValidUser();
            locked.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10);
            _userRepo.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(locked);

            var result = await Authorize(blocksUserId: "user-1");

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("account_locked");
        }

        [Fact]
        public async Task AuthorizeAsync_ReturnsAccountLocked_ViaBuildError_WhenLockedAfterClientCheck()
        {
            ClientExists();
            var locked = ValidUser();
            locked.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10);
            _userRepo.SetupSequence(u => u.GetUserByIdAsync(It.IsAny<string>()))
                .ReturnsAsync(ValidUser())   // first (lockout pre-check) passes
                .ReturnsAsync(locked);       // second (post client check) is locked

            var result = await Authorize(blocksUserId: "user-1", returnRedirectResponse: false);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("account_locked");
        }

        // ================= client / redirect_uri checks =================

        [Fact]
        public async Task AuthorizeAsync_ReturnsInvalidClient_WhenClientNotRegistered()
        {
            _userRepo.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync((User)null!);
            _authRepo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>()))
                .ReturnsAsync((OidcClientRegistration)null!);

            var result = await Authorize(blocksUserId: "user-1");

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_client");
        }

        [Fact]
        public async Task AuthorizeAsync_ReturnsInvalidRequest_WhenRedirectUriNotRegistered()
        {
            _userRepo.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(ValidUser());
            ClientExists("https://other.example.com/callback");

            var result = await Authorize(blocksUserId: "user-1");

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_request");
            Prop(bad.Value, "error_description").Should().Be("Invalid redirect_uri");
        }

        // ================= user-not-found error variants =================

        [Fact]
        public async Task AuthorizeAsync_ReturnsAccessDenied_WhenUserNotFound_NonRedirect()
        {
            ClientExists();
            _userRepo.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync((User)null!);

            var result = await Authorize(blocksUserId: "user-1", returnRedirectResponse: false);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("access_denied");
            Prop(bad.Value, "error_description").Should().Be("User not found");
        }

        [Fact]
        public async Task AuthorizeAsync_RedirectsWithAccessDenied_WhenUserNotFound_Redirect()
        {
            ClientExists();
            _userRepo.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync((User)null!);

            var result = await Authorize(blocksUserId: "user-1", returnRedirectResponse: true);

            var redirect = result.Should().BeOfType<RedirectResult>().Subject;
            redirect.Url.Should().StartWith(RedirectUri);
            redirect.Url.Should().Contain("error=access_denied");
        }

        // ================= code issuance (happy path) =================

        [Fact]
        public async Task AuthorizeAsync_IssuesCode_AndRedirects_OnValidRequest()
        {
            ClientExists();
            _userRepo.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(ValidUser());

            var result = await Authorize(blocksUserId: "user-1", returnRedirectResponse: true);

            var redirect = result.Should().BeOfType<RedirectResult>().Subject;
            redirect.Url.Should().StartWith(RedirectUri);
            redirect.Url.Should().Contain("code=authcode123");
            redirect.Url.Should().Contain("state=st");
            _authCodeRepo.Verify(a => a.CreateAsync(It.Is<AuthorizationCodeModel>(m =>
                m.Code == "authcode123"
                && m.ClientId == "client-1"
                && m.UserId == "user-1"
                && m.Scope == "openid profile offline_access"
                && !string.IsNullOrWhiteSpace(m.IdpSessionId))), Times.Once);
        }

        [Fact]
        public async Task AuthorizeAsync_StoresExistingIdpSessionId_OnAuthorizationCode()
        {
            ClientExists();
            _userRepo.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(ValidUser());
            _sessionRepo.Setup(s => s.GetBySessionIdAsync("SID"))
                .ReturnsAsync(Session("SID", new IdpSessionAccount { UserId = "user-1", TenantId = "tenant-1" }));

            var ctx = Ctx("idp_session_id_tenant-1=SID");
            await Authorize(ctx: ctx, tenant_id: "tenant-1");

            _authCodeRepo.Verify(a => a.CreateAsync(It.Is<AuthorizationCodeModel>(m =>
                m.IdpSessionId == "SID")), Times.Once);
        }

        [Fact]
        public async Task AuthorizeAsync_IssuesCode_ReturnsJson_WhenNotRedirect()
        {
            ClientExists();
            _userRepo.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(ValidUser());

            var result = await Authorize(blocksUserId: "user-1", returnRedirectResponse: false);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            (Prop(ok.Value, "redirect_uri") as string).Should().Contain("code=authcode123");
        }

        [Theory]
        [InlineData(UserMfaType.TOTP, "totp")]
        [InlineData(UserMfaType.Email, "otp")]
        public async Task AuthorizeAsync_AddsMfaAmr_WhenMfaCompleted(UserMfaType mfaType, string expectedAmr)
        {
            ClientExists();
            var user = ValidUser();
            user.UserMfaType = mfaType;
            _userRepo.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(user);

            await Authorize(blocksUserId: "user-1", mfaCompleted: true);

            _authCodeRepo.Verify(a => a.CreateAsync(It.Is<AuthorizationCodeModel>(m =>
                m.Amr.Contains("pwd") && m.Amr.Contains(expectedAmr))), Times.Once);
        }

        [Fact]
        public async Task AuthorizeAsync_SetsImpersonationFields_WhenPrincipalHasImpersonatedClaims()
        {
            ClientExists();
            _userRepo.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(ValidUser());
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("impersonated", "true"),
                new Claim("sub", "imp-user"),
                new Claim("tenant_id", "ten-x")
            }));
            _authService.Setup(s => s.GetPrincipalFromTokenAsync(It.IsAny<HttpRequest>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(principal);

            await Authorize(blocksUserId: "user-1");

            _authCodeRepo.Verify(a => a.CreateAsync(It.Is<AuthorizationCodeModel>(m =>
                m.Impersonated && m.ImpersonatedUserId == "imp-user" && m.TargetedTenantId == "ten-x")), Times.Once);
        }

        [Fact]
        public async Task AuthorizeAsync_PersistsLastUsedOrganization_WhenChanged()
        {
            ClientExists();
            var user = ValidUser();
            user.OrganizationIds = new List<string> { "org-1" };
            user.LastUsedOrganizationId = null;
            _userRepo.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(user);
            // Only a real organization is worth remembering, and only multi-org tenants have any:
            // with the mode off the scope is "default", a sentinel, which is never persisted here.
            _resourceRepo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });

            var result = await Authorize(blocksUserId: "user-1");

            result.Should().BeOfType<RedirectResult>();
            _userRepo.Verify(u => u.UpdateUserAsync(It.Is<User>(x => x.LastUsedOrganizationId == "org-1")), Times.Once);
        }

        [Fact]
        public async Task AuthorizeAsync_StillIssuesCode_WhenPersistOrganizationThrows()
        {
            ClientExists();
            var user = ValidUser();
            user.OrganizationIds = new List<string> { "org-1" };
            _userRepo.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(user);
            _userRepo.Setup(u => u.UpdateUserAsync(It.IsAny<User>())).ThrowsAsync(new Exception("db down"));

            var result = await Authorize(blocksUserId: "user-1", returnRedirectResponse: true);

            var redirect = result.Should().BeOfType<RedirectResult>().Subject;
            redirect.Url.Should().Contain("code=authcode123");
        }

        // ================= exception / server_error paths =================

        [Fact]
        public async Task AuthorizeAsync_Returns500_WhenRepositoryThrows_BeforeClientResolved()
        {
            _userRepo.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ThrowsAsync(new Exception("boom"));

            var result = await Authorize(blocksUserId: "user-1", returnRedirectResponse: false);

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(500);
            Prop(obj.Value, "error").Should().Be("server_error");
        }

        [Fact]
        public async Task AuthorizeAsync_RedirectsWithServerError_WhenThrowsAfterClientResolved()
        {
            ClientExists();
            _userRepo.SetupSequence(u => u.GetUserByIdAsync(It.IsAny<string>()))
                .ReturnsAsync(ValidUser())               // first (lockout pre-check) passes
                .ThrowsAsync(new Exception("boom"));     // second (after canRedirectToClient=true) throws

            var result = await Authorize(blocksUserId: "user-1", returnRedirectResponse: true);

            var redirect = result.Should().BeOfType<RedirectResult>().Subject;
            redirect.Url.Should().StartWith(RedirectUri);
            redirect.Url.Should().Contain("error=server_error");
        }
    }
}
