using Authentication.DomainService.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Security
{
    /// <summary>
    /// The stamps Genesis reads to refuse access tokens minted before an access change. The keys are a
    /// contract with Genesis' AccessStamps, and a failed write must never fail the change itself.
    /// </summary>
    public sealed class AccessStampWriterTests : IDisposable
    {
        private const string Tenant = "tenant-1";

        // The project lifetime in these tests (15 min) is below the per-client maximum, which then wins.
        private static readonly long StampSeconds = (OidcClientTokenLifetimes.MaxAccessTokenMinutes + 5) * 60L;

        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IAuthenticationRepository> _authRepo = new();

        public AccessStampWriterTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: Tenant, roles: null, userId: "admin-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: Tenant, impersonationSessionId: null, applicationDomain: "test"));
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync())
                .ReturnsAsync(new IdentityConfiguration { AccessTokenValidForNumberMinutes = 15 });
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private AccessStampWriter Create() => new(_cache.Object, _authRepo.Object, NullLogger<AccessStampWriter>.Instance);

        [Fact]
        public async Task Stamps_UseTheKeysGenesisReads_ForTheLongestAccessTokenLifetime()
        {
            var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var writer = Create();

            await writer.StampUserAsync("user-1");
            await writer.StampOrganizationAsync("org-a");
            await writer.StampSessionAsync("sid-1");

            foreach (var key in new[] { "access_changed:tenant-1:user-1", "org_disabled:tenant-1:org-a", "org_switched:sid-1" })
            {
                _cache.Verify(c => c.AddStringValueAsync(key,
                    It.Is<string>(v => long.Parse(v) >= before && long.Parse(v) <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                    StampSeconds), Times.Once);
            }
        }

        [Fact]
        public async Task AFailedWrite_NeverThrows()
        {
            // The change is already saved; the refresh still re-reads membership.
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                .ThrowsAsync(new InvalidOperationException("redis down"));

            var stamp = () => Create().StampUserAsync("user-1");

            await stamp.Should().NotThrowAsync();
        }
    }
}
