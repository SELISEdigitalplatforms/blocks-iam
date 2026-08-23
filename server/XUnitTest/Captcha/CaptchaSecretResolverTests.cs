using Blocks.CaptchaDriver;
using Blocks.Genesis;
using Blocks.Secrets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Captcha
{
    /// <summary>
    /// <see cref="CaptchaSecretResolver"/> behaviour: when it reaches the vault, when the cache
    /// answers instead, what identity the vault call runs under, and the fail-closed paths.
    /// </summary>
    /// <remarks>
    /// Every test drives <see cref="BlocksContext.IsTestMode"/> so the ambient context comes from
    /// AsyncLocal rather than a (non-existent) HTTP context.
    /// </remarks>
    public sealed class CaptchaSecretResolverTests : IDisposable
    {
        private const string TenantId = "tenant-1";
        private const string SecretId = "sec-1";
        private const string SecretValue = "6Ld3PJQtAAAAAGvKH4_EZeK3uiHunXh0-2qpvVQM";

        private readonly Mock<ISecretService> _secretService = new();
        private readonly Mock<ICacheClient> _cache = new();

        public CaptchaSecretResolverTests()
        {
            BlocksContext.IsTestMode = true;
            SetAmbientTenant(TenantId);

            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync((string)null!);
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                  .ReturnsAsync(true);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private static void SetAmbientTenant(string? tenantId, string organizationId = "org-1") =>
            BlocksContext.SetContext(tenantId is null
                ? null
                : BlocksContext.Create(
                    tenantId: tenantId,
                    roles: [],
                    userId: string.Empty,
                    isAuthenticated: false,
                    requestUri: "/api/authentication/recover",
                    organizationId: organizationId,
                    expireOn: DateTime.UtcNow.AddMinutes(5),
                    email: string.Empty,
                    permissions: [],
                    userName: string.Empty,
                    phoneNumber: string.Empty,
                    displayName: string.Empty,
                    oauthToken: string.Empty,
                    originalTenantId: tenantId));

        private CaptchaSecretResolver CreateResolver()
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => _secretService.Object);

            return new CaptchaSecretResolver(
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                _cache.Object,
                NullLogger<CaptchaSecretResolver>.Instance);
        }

        private static string ExpectedCacheKey => $"captcha:secret:{TenantId}:{SecretId}";

        #region Happy path

        [Fact]
        public async Task ResolveAsync_ColdCache_ReadsTheVaultOnceAndReturnsThePlaintext()
        {
            _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(SecretValue);

            var result = await CreateResolver().ResolveAsync(SecretId);

            result.Should().Be(SecretValue);
            _secretService.Verify(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ResolveAsync_AfterVaultRead_CachesTheValueForThirtyMinutes()
        {
            _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(SecretValue);

            await CreateResolver().ResolveAsync(SecretId);

            _cache.Verify(c => c.AddStringValueAsync(ExpectedCacheKey, SecretValue, 1800), Times.Once);
        }

        [Fact]
        public async Task ResolveAsync_WarmCache_ReturnsCachedValueWithoutTouchingTheVault()
        {
            _cache.Setup(c => c.GetStringValueAsync(ExpectedCacheKey)).ReturnsAsync(SecretValue);

            var result = await CreateResolver().ResolveAsync(SecretId);

            result.Should().Be(SecretValue);
            _secretService.Verify(
                s => s.GetValueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _cache.Verify(
                c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        }

        [Fact]
        public async Task ResolveAsync_RunsTheVaultCallUnderAnAuthenticatedNamedServiceContext()
        {
            BlocksContext? observed = null;
            _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .Callback(() => observed = BlocksContext.GetContext())
                          .ReturnsAsync(SecretValue);

            await CreateResolver().ResolveAsync(SecretId);

            observed.Should().NotBeNull();
            observed!.IsAuthenticated.Should().BeTrue();
            observed.TenantId.Should().Be(TenantId);
            observed.OrganizationId.Should().Be("org-1");
            // The audit row's actor: a named identity, never blank.
            observed.UserId.Should().Be("blocks-iam-captcha");
        }

        [Fact]
        public async Task ResolveAsync_WithNoAmbientOrganization_DefaultsToDefault()
        {
            SetAmbientTenant(TenantId, organizationId: string.Empty);

            BlocksContext? observed = null;
            _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .Callback(() => observed = BlocksContext.GetContext())
                          .ReturnsAsync(SecretValue);

            await CreateResolver().ResolveAsync(SecretId);

            observed!.OrganizationId.Should().Be("default");
        }

        #endregion

        #region Critical path

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task ResolveAsync_WithNoSecretId_ReturnsNullAndTouchesNothing(string? secretId)
        {
            var result = await CreateResolver().ResolveAsync(secretId);

            result.Should().BeNull();
            _cache.Verify(c => c.GetStringValueAsync(It.IsAny<string>()), Times.Never);
            _secretService.Verify(
                s => s.GetValueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ResolveAsync_WithNoTenant_FailsClosedWithoutCallingTheVault()
        {
            SetAmbientTenant(null);

            var result = await CreateResolver().ResolveAsync(SecretId);

            result.Should().BeNull();
            _secretService.Verify(
                s => s.GetValueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        public static TheoryData<Exception> StoreFailures() =>
        [
            new SecretNotFoundException(SecretId),
            new SecretAccessDeniedException("not-in-access-list"),
            new SecretVaultException("vault unreachable", "Get", SecretId, new InvalidOperationException()),
            new InvalidOperationException("something unexpected")
        ];

        [Theory]
        [MemberData(nameof(StoreFailures))]
        public async Task ResolveAsync_WhenTheStoreFails_ReturnsNullAndCachesNothing(Exception failure)
        {
            _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .ThrowsAsync(failure);

            var result = await CreateResolver().ResolveAsync(SecretId);

            result.Should().BeNull();
            _cache.Verify(
                c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        }

        [Fact]
        public async Task ResolveAsync_AfterAFailure_RetriesTheVaultOnTheNextCall()
        {
            _secretService.SetupSequence(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .ThrowsAsync(new SecretVaultException("down", "Get", SecretId, new InvalidOperationException()))
                          .ReturnsAsync(SecretValue);

            var resolver = CreateResolver();

            (await resolver.ResolveAsync(SecretId)).Should().BeNull();
            (await resolver.ResolveAsync(SecretId)).Should().Be(SecretValue);

            _secretService.Verify(
                s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task ResolveAsync_WhenTheCacheReadThrows_TreatsItAsAMissAndContinues()
        {
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>()))
                  .ThrowsAsync(new InvalidOperationException("redis down"));
            _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(SecretValue);

            var result = await CreateResolver().ResolveAsync(SecretId);

            result.Should().Be(SecretValue);
        }

        [Fact]
        public async Task ResolveAsync_WhenTheCacheWriteThrows_StillReturnsTheResolvedValue()
        {
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                  .ThrowsAsync(new InvalidOperationException("redis down"));
            _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(SecretValue);

            var result = await CreateResolver().ResolveAsync(SecretId);

            result.Should().Be(SecretValue);
        }

        [Fact]
        public async Task ResolveAsync_WhenTheStoreReturnsEmpty_ReturnsNullAndCachesNothing()
        {
            _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(string.Empty);

            var result = await CreateResolver().ResolveAsync(SecretId);

            result.Should().BeNull();
            _cache.Verify(
                c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        }

        #endregion

        #region Composition

        /// <summary>
        /// The resolver is a singleton while <c>ISecretService</c> is scoped. Building the provider
        /// with scope validation is what would surface a captive dependency, so this test is the
        /// guard against reintroducing one by taking ISecretService on the constructor.
        /// </summary>
        [Fact]
        public void Resolver_IsASingletonThatDoesNotCaptureTheScopedSecretService()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(_cache.Object);
            services.AddScoped(_ => _secretService.Object);
            services.AddSingleton<ICaptchaSecretResolver, CaptchaSecretResolver>();

            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });

            var first = provider.GetRequiredService<ICaptchaSecretResolver>();
            var second = provider.GetRequiredService<ICaptchaSecretResolver>();

            first.Should().BeSameAs(second);
        }

        #endregion
    }
}
