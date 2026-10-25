using Blocks.CaptchaDriver;
using Blocks.Genesis;
using Blocks.Secrets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Captcha
{
    /// <summary>
    /// <see cref="CaptchaSecretResolver"/> behaviour: when it reaches the vault, what identity the vault call runs under, and the fail-closed paths.
    /// </summary>
    /// <remarks>
    /// Every test drives <see cref="BlocksContext.IsTestMode"/> so the ambient context comes from
    /// AsyncLocal rather than a (non-existent) HTTP context.
    /// </remarks>
    public sealed class CaptchaSecretResolverTests : IDisposable
    {
        private const string TenantId = "tenant-1";
        private const string SecretId = "sec-1";
        private const string SecretValue = "test-captcha-secret";

        private readonly Mock<ISecretService> _secretService = new();

        public CaptchaSecretResolverTests()
        {
            BlocksContext.IsTestMode = true;
            SetAmbientTenant(TenantId);
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

        private CaptchaSecretResolver CreateResolver(ILogger<CaptchaSecretResolver>? logger = null, IServiceScopeFactory? scopeFactory = null)
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => _secretService.Object);

            return new CaptchaSecretResolver(
                scopeFactory ?? services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                logger ?? NullLogger<CaptchaSecretResolver>.Instance);
        }

        private sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
        {
            public int Created;

            public IServiceScope CreateScope()
            {
                Interlocked.Increment(ref Created);
                return inner.CreateScope();
            }
        }

        private sealed class CapturingLogger : ILogger<CaptchaSecretResolver>
        {
            public List<string> Messages { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                Messages.Add(formatter(state, exception));
                Messages.Add(state?.ToString() ?? string.Empty);
                if (exception is not null)
                {
                    Messages.Add(exception.ToString());
                }
            }
        }

        #region Happy path

        [Fact]
        public async Task ResolveAsync_ReadsTheStoreAndReturnsThePlaintext()
        {
            _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(SecretValue);

            var result = await CreateResolver().ResolveAsync(SecretId);

            result.Should().Be(SecretValue);
            _secretService.Verify(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ResolveAsync_CalledTwice_ReadsTheStoreTwice()
        {
            _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(SecretValue);

            var resolver = CreateResolver();
            await resolver.ResolveAsync(SecretId);
            await resolver.ResolveAsync(SecretId);

            _secretService.Verify(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task ResolveAsync_UsesANewScopePerCall()
        {
            _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(SecretValue);
            var services = new ServiceCollection();
            services.AddScoped(_ => _secretService.Object);
            var factory = new CountingScopeFactory(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

            var resolver = CreateResolver(scopeFactory: factory);
            await resolver.ResolveAsync(SecretId);
            await resolver.ResolveAsync(SecretId);

            factory.Created.Should().Be(2);
        }

        [Fact]
        public async Task ResolveAsync_ConcurrentTenants_EachReadUnderItsOwnTenant()
        {
            _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .Returns(async () =>
                          {
                              await Task.Delay(20);
                              return "value-of-" + BlocksContext.GetContext()!.TenantId;
                          });
            var resolver = CreateResolver();

            async Task<string?> RunAs(string tenant)
            {
                SetAmbientTenant(tenant);
                return await resolver.ResolveAsync(SecretId);
            }

            var results = await Task.WhenAll(Task.Run(() => RunAs("tenant-a")), Task.Run(() => RunAs("tenant-b")));

            results[0].Should().Be("value-of-tenant-a");
            results[1].Should().Be("value-of-tenant-b");
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
            new SecretStateException(SecretStatuses.Locked, "read value"),
            new InvalidOperationException("something unexpected")
        ];

        [Theory]
        [MemberData(nameof(StoreFailures))]
        public async Task ResolveAsync_WhenTheStoreFails_ReturnsNull(Exception failure)
        {
            _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .ThrowsAsync(failure);

            var result = await CreateResolver().ResolveAsync(SecretId);

            result.Should().BeNull();
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
        public async Task ResolveAsync_WhenTheStoreReturnsEmpty_ReturnsNull()
        {
            _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(string.Empty);

            var result = await CreateResolver().ResolveAsync(SecretId);

            result.Should().BeNull();
        }

        [Fact]
        public async Task ResolveAsync_WhenCancelled_Throws()
        {
            _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .ThrowsAsync(new OperationCanceledException());

            var act = () => CreateResolver().ResolveAsync(SecretId);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task ResolveAsync_OnEveryFailurePath_NeverLogsTheValue()
        {
            var logger = new CapturingLogger();
            var resolver = CreateResolver(logger);

            SetAmbientTenant(null);
            await resolver.ResolveAsync(SecretId);
            SetAmbientTenant(TenantId);

            _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(string.Empty);
            await resolver.ResolveAsync(SecretId);

            foreach (var failure in ((IEnumerable<object[]>)StoreFailures()).Select(d => (Exception)d[0]))
            {
                _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                              .ThrowsAsync(failure);
                await resolver.ResolveAsync(SecretId);
            }

            // Success path: the store returns the value; it must reach the caller but never the logs.
            _secretService.Setup(s => s.GetValueAsync(SecretId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(SecretValue);
            (await resolver.ResolveAsync(SecretId)).Should().Be(SecretValue);

            logger.Messages.Should().NotBeEmpty();
            logger.Messages.Should().NotContain(m => m.Contains(SecretValue, StringComparison.Ordinal));
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
