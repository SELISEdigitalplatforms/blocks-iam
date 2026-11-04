using Authentication.DomainService.Authentication.LoginPolicies;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Resources;
using Iam.DomainService.Shared.Entities;
using Moq;

namespace XUnitTest.Auth.LoginPolicies
{
    public class LoginPolicyServiceTests : IDisposable
    {
        private const string TenantId = "tenant-1";
        private readonly Mock<IResourceRepository> _resources = new();
        private readonly Mock<IAuthenticationRepository> _auth = new();
        private readonly Mock<ITenants> _tenants = new();

        public LoginPolicyServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: TenantId, roles: null, userId: null, impersonated: false,
                isAuthenticated: false, requestUri: "https://test", organizationId: null,
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: null,
                userName: null, phoneNumber: null, displayName: null, oauthToken: null,
                originalTenantId: TenantId, impersonationSessionId: null, applicationDomain: "test"));
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(Tenant(isRoot: false));
            _auth.Setup(a => a.GetIdentityProvidersAsync()).ReturnsAsync(
            [
                Provider("p1", "social", true),
                Provider("p2", "social", false),
                Provider("p3", "enterprise", true)
            ]);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private static Tenant Tenant(bool isRoot) => new()
        {
            TenantId = TenantId,
            IsRootTenant = isRoot,
            DbConnectionString = string.Empty,
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow }
        };

        private static IdentityProvider Provider(string id, string type, bool active) => new()
        {
            ItemId = id, Provider = "google", ProviderType = type, IsActive = active,
            ClientId = id, ClientSecret = "s", TokenEndpointAuthMethod = "client_secret_post"
        };

        private LoginPolicyService Create() => new(_resources.Object, _auth.Object, _tenants.Object);

        private void TenantPolicy(TenantLoginPolicy? policy) =>
            _resources.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new IamTenantConfiguration { LoginPolicy = policy });

        [Fact]
        public async Task CheckAsync_UsesTheTenantPolicy()
        {
            TenantPolicy(new TenantLoginPolicy { AllowPassword = false });

            (await Create().CheckAsync(LoginChannel.Embedded, LoginMethods.Password)).IsAllowed.Should().BeFalse();
            (await Create().CheckAsync(LoginChannel.Embedded, LoginMethods.Social)).IsAllowed.Should().BeTrue();
        }

        [Fact]
        public async Task CheckAsync_UsesTheClientPolicy()
        {
            TenantPolicy(null);
            var client = new OidcClientRegistration { LoginPolicy = new OidcClientLoginPolicy { Method = OidcClientLoginMethods.Social } };

            (await Create().CheckAsync(LoginChannel.Oidc, LoginMethods.Password, client)).IsAllowed.Should().BeFalse();
        }

        [Fact]
        public async Task NoTenantConfiguration_AllowsEverything()
        {
            _resources.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync((IamTenantConfiguration)null!);

            (await Create().CheckAsync(LoginChannel.Oidc, LoginMethods.Password)).IsAllowed.Should().BeTrue();
            (await Create().CheckChannelAsync(LoginChannel.Embedded)).IsAllowed.Should().BeTrue();
        }

        [Fact]
        public async Task RootTenant_IsNeverLimited_AndPolicyIsNotRead()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(Tenant(isRoot: true));
            TenantPolicy(new TenantLoginPolicy { AllowOidc = false, AllowPassword = false });

            var service = Create();
            (await service.CheckAsync(LoginChannel.Oidc, LoginMethods.Password)).IsAllowed.Should().BeTrue();
            (await service.CheckChannelAsync(LoginChannel.Oidc)).IsAllowed.Should().BeTrue();
            (await service.SessionAccountFilterAsync(null))(new IdpSessionAccount { LoginMethod = LoginMethods.Password }).Should().BeTrue();
            (await service.GetOptionsAsync(LoginChannel.Oidc, null)).Password.Should().BeTrue();
            _resources.Verify(r => r.GetTenantConfigurationAsync(), Times.Never);
        }

        [Fact]
        public async Task GetOptionsAsync_OnlyActiveSocialProviders()
        {
            TenantPolicy(null);

            var options = await Create().GetOptionsAsync(LoginChannel.Embedded, null);

            options.Password.Should().BeTrue();
            options.Social.Should().BeTrue();
            options.Providers.Select(p => p.ItemId).Should().Equal("p1");
        }

        [Fact]
        public async Task SessionAccountFilter_FollowsTheClientMethod()
        {
            TenantPolicy(null);
            var client = new OidcClientRegistration { LoginPolicy = new OidcClientLoginPolicy { Method = OidcClientLoginMethods.Password } };

            var allowed = await Create().SessionAccountFilterAsync(client);

            allowed(new IdpSessionAccount { LoginMethod = LoginMethods.Social }).Should().BeFalse();
            allowed(new IdpSessionAccount { LoginMethod = LoginMethods.Password }).Should().BeTrue();
            allowed(new IdpSessionAccount()).Should().BeTrue();
        }
    }
}
