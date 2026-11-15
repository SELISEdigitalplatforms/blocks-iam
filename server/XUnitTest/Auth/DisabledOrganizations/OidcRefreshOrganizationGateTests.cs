using Authentication.DomainService.Authentication;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;
using XUnitTest.TestDoubles;

namespace XUnitTest.Auth.DisabledOrganizations
{
    /// <summary>
    /// Spec #668 on <c>oidc/token</c> refresh: <see cref="OidcRefreshTokenService"/> over the real
    /// <see cref="RefreshTokenAuthenticationService"/> and <see cref="JwtAccessTokenProvider"/>. C1 (hint is explicit),
    /// C2 (refused refresh neither rotates nor revokes), C4, H4 (org_status in the refresh body), H7.
    /// </summary>
    public sealed class OidcRefreshOrganizationGateTests : IDisposable
    {
        private const string A = "org-a";
        private const string D = "org-d";

        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IAuthenticationService> _authService = new();
        private readonly Mock<IRefreshTokenRepository> _refreshTokenRepo = new();
        private readonly Mock<IRefreshSessionResolver> _sessionResolver = new();
        private readonly Mock<IOAuthJwtAccessTokenManager> _tokenManager = new();
        private readonly Mock<IResourceRepository> _resources = new();

        public OidcRefreshOrganizationGateTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));

            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(DisabledOrganizationsKit.SigningTenant());
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration { AccessTokenValidForNumberMinutes = 10 });
            _authRepo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(new OidcClientRegistration { ClientId = "c1", UseTokensCookie = false });
            _sessionResolver
                .Setup(r => r.ResolveRefreshSessionAsync(It.IsAny<string>(), It.IsAny<IdentityConfiguration>(), It.IsAny<bool>()))
                .ReturnsAsync((string _, IdentityConfiguration _, bool _) => new RefreshSessionResolution(_cache, false));
            _tokenManager.Setup(m => m.ManageRefreshTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<JwtAccessToken>(), It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>()))
                .ReturnsAsync(("new-refresh-token", DateTime.UtcNow.AddMinutes(30)));
            _resources.MultiOrg();
        }

        private RefreshTokenCache? _cache;

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private OidcRefreshTokenService Create() =>
            new(_authRepo.Object, _tenants.Object,
                new RefreshTokenAuthenticationService(NullLogger<RefreshTokenAuthenticationService>.Instance,
                    DisabledOrganizationsKit.RealProvider(_resources), _tenants.Object, _tokenManager.Object, _authRepo.Object),
                _authService.Object, _refreshTokenRepo.Object, _sessionResolver.Object, NullLogger<OidcRefreshTokenService>.Instance);

        private void Arrange(string? hint, params string[] memberships)
        {
            _cache = new RefreshTokenCache
            {
                UserId = "u1", ClientId = "c1", TenantId = "tenant-1", OrganizationId = hint,
                RefreshToken = "rt-1", RefreshTokenSessionId = "lineage-1"
            };
            _authRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(new User
            {
                ItemId = "u1", Active = true, IsVerified = true, Status = UserLifecycleStatus.Active, OrganizationIds = [.. memberships]
            });
        }

        private Task<IActionResult> Refresh()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Form = new FormCollection(new Dictionary<string, StringValues> { ["client_id"] = "c1", ["refresh_token"] = "rt-1" });
            return Create().RotateAsync(ctx.Request);
        }

        private static object? Prop(object? value, string name) => value?.GetType().GetProperty(name)?.GetValue(value);

        private void VerifyLineageUntouched()
        {
            _tokenManager.Verify(m => m.ManageRefreshTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<JwtAccessToken>(), It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>()), Times.Never);
            _refreshTokenRepo.Verify(r => r.RevokeLineageAndEvictAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task C1_C2_OidcRefreshHint_Disabled_403_NoRotationNoRevoke()
        {
            Arrange(D, D, A);
            _resources.WithOrganizationStatuses(disabled: [D]);

            var result = await Refresh();

            var refusal = result.Should().BeOfType<ObjectResult>().Subject;
            refusal.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            Prop(refusal.Value, "error").Should().Be("organization_disabled");
            VerifyLineageUntouched();
        }

        [Fact]
        public async Task C4_OidcRefresh_StatusReadThrows_503_NoRotation()
        {
            Arrange(A, A);
            _resources.Setup(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>())).ThrowsAsync(new TimeoutException());

            var result = await Refresh();

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
            VerifyLineageUntouched();
        }

        [Fact]
        public async Task H7_OidcRefreshHint_ReEnabled_IssuedForIt_AndTheRotationRemembersIt()
        {
            Arrange(D, D, A);
            _resources.WithActiveOrganizations();

            var result = await Refresh();

            var body = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
            body.Should().NotContainKey("org_status");
            _tokenManager.Verify(m => m.ManageRefreshTokenAsync(It.Is<TokenRequest>(t => t.OrganizationId == D),
                It.IsAny<JwtAccessToken>(), It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>()), Times.Once);
        }

        [Fact]
        public async Task H4_OidcRefresh_AllDisabled_SettingTrue_OrgStatusInBody()
        {
            _resources.MultiOrg(allowNoOrg: true);
            Arrange("no-org", D);
            _resources.WithOrganizationStatuses(disabled: [D]);

            var result = await Refresh();

            var body = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
            body["org_status"].Should().Be("disabled");
        }
    }
}
