using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Blocks.CaptchaDriver;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Mfa.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XUnitTest.TestDoubles;
using XUnitTest.TestSupport;

namespace XUnitTest.Auth.DisabledOrganizations
{
    /// <summary>
    /// Spec #668 Q7: an <c>oidc/login</c> whose authorize is refused for organization status answers 403 JSON and
    /// writes no LoginSuccess audit. The orchestrator drives the real <see cref="OidcAuthorizationEndpoint"/>.
    /// </summary>
    public sealed class OidcLoginOrchestratorOrganizationRefusalTests : IDisposable
    {
        private const string TenantId = "tenant-1";
        private const string Plaintext = "Secret123!";
        private const string RedirectUri = "https://app.example.com/callback";

        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IMfaChallengeIssuer> _mfaIssuer = new();
        private readonly Mock<Authentication.DomainService.Authentication.IAuthenticationService> _authService = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IAuthenticationDomainService> _authDomain = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();
        private readonly Mock<IResourceRepository> _resources = new();
        private readonly Mock<IAuthorizationCodeRepository> _authCodeRepo = new();
        private readonly Mock<IIdpSessionRepository> _sessionRepo = new();
        private readonly Mock<IPkceService> _pkce = new();
        private readonly FakeLoginPolicy _loginPolicy = new();

        public OidcLoginOrchestratorOrganizationRefusalTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: TenantId, roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: TenantId, impersonationSessionId: null, applicationDomain: "test"));

            _activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);
            _authDomain.Setup(d => d.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation)null!);
            _repo.Setup(r => r.TryReserveLoginAttemptAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new User { ItemId = "user-1", FailedLoginCount = 1 });
            _repo.Setup(r => r.ReleaseLoginAttemptAsync(It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(true);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>()))
                .ReturnsAsync(new OidcClientRegistration { ClientId = "client-1", RedirectUris = [RedirectUri], RequirePkce = false });
            _mfaIssuer.Setup(m => m.IsRequiredAsync(It.IsAny<User>())).ReturnsAsync(false);
            _pkce.Setup(p => p.GenerateRandomCode(It.IsAny<int>())).Returns("authcode123");
            _sessionRepo.Setup(s => s.CreateAsync(It.IsAny<IdpSessionModel>())).ReturnsAsync("new-sess");
            _sessionRepo.Setup(s => s.AddAccountAsync(It.IsAny<string>(), It.IsAny<IdpSessionAccount>())).ReturnsAsync(true);
            _authCodeRepo.Setup(a => a.CreateAsync(It.IsAny<AuthorizationCodeModel>())).ReturnsAsync("code-id");
            _resources.MultiOrg();
            _resources.WithOrganizationStatuses(disabled: ["org-d"]);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private OidcLoginOrchestrator Create()
        {
            var endpoint = new OidcAuthorizationEndpoint(
                new OidcAuthorizationSessionStores(_authCodeRepo.Object, _sessionRepo.Object,
                    new Mock<Authentication.DomainService.Oidc.Services.IIdpSessionService>().Object, _pkce.Object, new Mock<ILinkSessionRepository>().Object),
                new OidcAuthorizationIdentityStores(_userRepo.Object, _repo.Object, _authService.Object, _tenants.Object, _cache.Object, _resources.Object, _loginPolicy),
                NullLogger<OidcAuthorizationEndpoint>.Instance);

            return new OidcLoginOrchestrator(_repo.Object, _mfaIssuer.Object, _authService.Object, _tenants.Object, _cache.Object,
                _userRepo.Object, new PasswordHasher(NullLogger<PasswordHasher>.Instance),
                new OidcLoginAuditWriter(_authDomain.Object, _activity.Object, NullLogger<OidcLoginAuditWriter>.Instance),
                new OidcCaptchaEvaluator(new Mock<ICaptchaEvaluator>().Object), endpoint,
                NullLogger<OidcLoginOrchestrator>.Instance, UnknownLoginGuardStub.Create().Object, _loginPolicy);
        }

        private void UserWith(params string[] organizationIds)
        {
            var user = new User
            {
                ItemId = "user-1", Email = "u@example.com", Active = true, IsVerified = true,
                Password = BCrypt.Net.BCrypt.HashPassword(Plaintext, 4), UserMfaType = UserMfaType.Email,
                OrganizationIds = [.. organizationIds]
            };
            _repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(user);
            _userRepo.Setup(u => u.GetUserByIdAsync("user-1")).ReturnsAsync(user);
        }

        private Task<IActionResult> Login() =>
            Create().ExecuteAsync(new OidcLoginRequest
            {
                Username = "u@example.com", Password = Plaintext, ClientId = "client-1",
                RedirectUri = RedirectUri, TenantId = TenantId, Scope = "openid profile"
            }, new DefaultHttpContext().Request, new DefaultHttpContext().Response);

        [Fact]
        public async Task Q7_RefusedOidcLogin_403Json_NoLoginSuccessAudit_NoCode()
        {
            UserWith("org-d");

            var result = await Login();

            result.Should().BeOfType<OrganizationRefusalResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(e => e.Outcome == "success")), Times.Never);
            _authCodeRepo.Verify(a => a.CreateAsync(It.IsAny<AuthorizationCodeModel>()), Times.Never);
        }

        [Fact]
        public async Task Q7_ActiveOrganization_OidcLogin_StillWritesLoginSuccessAudit()
        {
            UserWith("org-a");

            var result = await Login();

            result.Should().BeOfType<OkObjectResult>();
            _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(e => e.Outcome == "success")), Times.AtLeastOnce);
            _authCodeRepo.Verify(a => a.CreateAsync(It.Is<AuthorizationCodeModel>(m => m.OrganizationId == "org-a")), Times.Once);
        }
    }
}
