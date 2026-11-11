using System.Security.Claims;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.Auth.DisabledOrganizations
{
    /// <summary>Spec #668 H5: <c>GET auth/me</c> echoes <c>org_status</c> from the token, like <c>org_id</c>, and only when present.</summary>
    public sealed class AuthMeOrganizationStatusTests : IDisposable
    {
        private readonly Mock<IAuthenticationRepository> _repo = new();

        public AuthMeOrganizationStatusTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
            _repo.Setup(r => r.GetUserByIdAsync("user-9")).ReturnsAsync(new User { ItemId = "user-9" });
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private AuthenticationService Create() =>
            new(NullLogger<AuthenticationService>.Instance, new Mock<ICacheClient>().Object, _repo.Object, new Mock<IAuthenticationDomainService>().Object,
                new Mock<IAuthSessionFacade>().Object, new Mock<ITenants>().Object, new Mock<IUserActivityDispatcher>().Object,
                new Mock<IRefreshTokenRepository>().Object, new FakeLoginPolicy());

        private static ClaimsPrincipal Principal(string? organizationStatus)
        {
            var claims = new List<Claim>
            {
                new("sub", "user-9"),
                new("scope", "openid"),
                new(BlocksContext.ORGANIZATION_ID_CLAIM, "no-org")
            };
            if (organizationStatus != null)
            {
                claims.Add(new Claim("org_status", organizationStatus));
            }

            return new ClaimsPrincipal(new ClaimsIdentity(claims));
        }

        [Fact]
        public async Task H5_AuthMe_ReturnsOrgStatus_WhenTheTokenCarriesIt()
        {
            var (isValid, info) = await Create().BuildOidcUserInfoAsync(Principal("disabled"));

            isValid.Should().BeTrue();
            info["org_status"].Should().Be("disabled");
            info[BlocksContext.ORGANIZATION_ID_CLAIM].Should().Be("no-org");
        }

        [Fact]
        public async Task H5_AuthMe_OmitsOrgStatus_WhenTheTokenHasNone()
        {
            var (isValid, info) = await Create().BuildOidcUserInfoAsync(Principal(null));

            isValid.Should().BeTrue();
            info.Should().NotContainKey("org_status");
        }
    }
}
