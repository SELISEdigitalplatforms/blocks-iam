using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Moq;

namespace XUnitTest.Auth
{
    /// <summary>
    /// <see cref="TokenRefresher"/> is a thin seam over the cache, tenant store and token manager.
    /// These tests assert each method delegates to the correct dependency and returns its result.
    /// </summary>
    public class TokenRefresherTests
    {
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IOAuthJwtAccessTokenManager> _tokenManager = new();

        // The refresh-token authentication service dependency is stored but never used by any
        // TokenRefresher method, so it is not needed for these delegation tests.
        private TokenRefresher Create() =>
            new(_cache.Object, _tenants.Object, null!, _tokenManager.Object);

        [Fact]
        public async Task GetCacheValueAsync_DelegatesToCache()
        {
            _cache.Setup(c => c.GetStringValueAsync("k")).ReturnsAsync("v");

            var result = await Create().GetCacheValueAsync("k");

            result.Should().Be("v");
            _cache.Verify(c => c.GetStringValueAsync("k"), Times.Once);
        }

        [Fact]
        public async Task RemoveKeyAsync_DelegatesToCache()
        {
            _cache.Setup(c => c.RemoveKeyAsync("k")).ReturnsAsync(true);

            await Create().RemoveKeyAsync("k");

            _cache.Verify(c => c.RemoveKeyAsync("k"), Times.Once);
        }

        [Fact]
        public async Task GetTenantByIDAsync_DelegatesToTenants()
        {
            var tenant = new Tenant
            {
                TenantId = "tenant-1",
                DbConnectionString = string.Empty,
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow }
            };
            _tenants.Setup(t => t.GetTenantByID("tenant-1")).Returns(tenant);

            var result = await Create().GetTenantByIDAsync("tenant-1");

            result.Should().BeSameAs(tenant);
            _tenants.Verify(t => t.GetTenantByID("tenant-1"), Times.Once);
        }

        [Fact]
        public async Task AuthenticateAsync_DelegatesToTokenManager()
        {
            var request = new TokenRequest();
            var config = new IdentityConfiguration();
            var user = new User { ItemId = "u1" };
            var response = new TokenResponse { AccessToken = "at" };
            _tokenManager.Setup(m => m.ManageTokenAsync(request, config, user, null)).ReturnsAsync(response);

            var result = await Create().AuthenticateAsync(request, config, user);

            result.Should().BeSameAs(response);
            _tokenManager.Verify(m => m.ManageTokenAsync(request, config, user, null), Times.Once);
        }
    }
}
