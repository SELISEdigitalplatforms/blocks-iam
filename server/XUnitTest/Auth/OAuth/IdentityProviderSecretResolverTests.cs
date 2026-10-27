using System.Collections.Concurrent;
using System.Security.Claims;
using Authentication.DomainService.OAuth;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Blocks.Secrets;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XUnitTest.TestDoubles;

namespace XUnitTest.Auth.OAuth
{
    /// <summary>
    /// <see cref="IdentityProviderSecretResolver"/> (blocks-iam#649): the reference rule, the context
    /// a sign-in read runs under and its restore, the failure classes, and what reaches the logs.
    /// </summary>
    public sealed class IdentityProviderSecretResolverTests : IDisposable
    {
        private const string TenantId = "tenant-a";
        private const string Reference = "ref-7f3a";
        private const string VaultValue = "vault-value-9c1e";
        private const string LegacyValue = "legacy-plaintext";

        private readonly Mock<ISecretService> _secrets = new(MockBehavior.Strict);

        public IdentityProviderSecretResolverTests()
        {
            BlocksContext.IsTestMode = true;
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null, false);
            BlocksContext.IsTestMode = false;
        }

        private static IdentityProvider Provider(string? clientSecretReference = null, string? privateKeyReference = null) => new()
        {
            ItemId = "idp-1",
            Provider = "google",
            ProviderType = "social",
            ClientId = "cid",
            TokenEndpointAuthMethod = "client_secret_post",
            ClientSecret = LegacyValue,
            ClientSecretReference = clientSecretReference,
            PrivateKey = "legacy-pem",
            PrivateKeyReference = privateKeyReference,
        };

        private static BlocksContext CallerContext(string tenantId = "caller-tenant") =>
            BlocksContext.Create(tenantId, [], "caller-user", true, "/api/idp", "org", DateTime.UtcNow.AddMinutes(5),
                "caller@test.com", [], "caller", "", "Caller", "token", tenantId);

        private sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
        {
            public int Created;

            public IServiceScope CreateScope()
            {
                Interlocked.Increment(ref Created);
                return inner.CreateScope();
            }
        }

        private (IdentityProviderSecretResolver Resolver, CountingScopeFactory Scopes) CountingResolver()
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => _secrets.Object);
            var scopes = new CountingScopeFactory(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }).GetRequiredService<IServiceScopeFactory>());
            return (new IdentityProviderSecretResolver(scopes, NullLogger<IdentityProviderSecretResolver>.Instance), scopes);
        }

        // ---------- reference rule ----------

        [Fact]
        public async Task LegacyProvider_ReturnsThePlaintextFields_WithoutAScopeOrSecretCall()
        {
            // H7, C13
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);
            var (sut, scopes) = CountingResolver();

            (await sut.GetClientSecretAsync(Provider())).Should().Be(LegacyValue);
            (await sut.GetPrivateKeyAsync(Provider())).Should().Be("legacy-pem");

            scopes.Created.Should().Be(0);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task LegacyProvider_WithNoAmbientTenant_StillResolves()
        {
            // C3: legacy providers keep working whatever the secret store's state.
            var (sut, _) = CountingResolver();

            (await sut.GetClientSecretAsync(Provider())).Should().Be(LegacyValue);
        }

        [Fact]
        public async Task ReferencedClientSecret_IsReadOnce_AndTheLegacyFieldIsIgnored()
        {
            // H5, C4, C13
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ReturnsAsync(VaultValue);
            var (sut, scopes) = CountingResolver();

            (await sut.GetClientSecretAsync(Provider(clientSecretReference: Reference))).Should().Be(VaultValue);

            scopes.Created.Should().Be(1);
            _secrets.Verify(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ReferencedPrivateKey_IsRead_AndMixedStateKeepsTheLegacyClientSecret()
        {
            // H6; per-field mixed state (Apple with a referenced key and a legacy client secret).
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);
            _secrets.Setup(s => s.GetValueAsync("pk-ref", It.IsAny<CancellationToken>())).ReturnsAsync("vault-pem");
            var sut = IdentityProviderSecretTestDoubles.Resolver(_secrets);
            var provider = Provider(privateKeyReference: "pk-ref");

            (await sut.GetPrivateKeyAsync(provider)).Should().Be("vault-pem");
            (await sut.GetClientSecretAsync(provider)).Should().Be(LegacyValue);
            _secrets.Verify(s => s.GetValueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EveryCall_ReadsTheStoreAgain_InAFreshScope()
        {
            // No plaintext cache; ISecretService is scoped, the resolver a singleton.
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ReturnsAsync(VaultValue);
            var (sut, scopes) = CountingResolver();

            await sut.GetClientSecretAsync(Provider(clientSecretReference: Reference));
            await sut.GetClientSecretAsync(Provider(clientSecretReference: Reference));

            scopes.Created.Should().Be(2);
            _secrets.Verify(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public void Resolver_IsASingletonThatDoesNotCaptureTheScopedSecretService()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped(_ => _secrets.Object);
            services.AddSingleton<IIdentityProviderSecretResolver, IdentityProviderSecretResolver>();
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

            provider.GetRequiredService<IIdentityProviderSecretResolver>()
                .Should().BeSameAs(provider.GetRequiredService<IIdentityProviderSecretResolver>());
        }

        // ---------- context (H13) ----------

        [Fact]
        public async Task AnonymousRequest_ReadsInASynthesizedServiceContextForTheAmbientTenant_AndRestoresIt()
        {
            // H13, C9
            var ambient = IdentityProviderSecretTestDoubles.AnonymousContext(TenantId);
            BlocksContext.SetContext(ambient, false);
            BlocksContext? seen = null;
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()))
                .Callback(() => seen = BlocksContext.GetContext())
                .ReturnsAsync(VaultValue);

            await IdentityProviderSecretTestDoubles.Resolver(_secrets).GetClientSecretAsync(Provider(clientSecretReference: Reference));

            seen.Should().NotBeNull();
            seen!.IsAuthenticated.Should().BeTrue();
            seen.TenantId.Should().Be(TenantId);
            seen.OriginalTenantId.Should().Be(TenantId);
            seen.UserId.Should().Be(IdentityProviderSecretResolver.ServiceUserId);
            seen.OrganizationId.Should().Be("org-1");
            BlocksContext.GetContext().Should().BeSameAs(ambient);
        }

        [Fact]
        public async Task AuthenticatedCaller_IsTheAuditActor_AndTheContextIsNotReplaced()
        {
            // H13: "unless the caller is already authenticated"
            var caller = CallerContext();
            BlocksContext.SetContext(caller, true);
            BlocksContext? seen = null;
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()))
                .Callback(() => seen = BlocksContext.GetContext())
                .ReturnsAsync(VaultValue);

            await IdentityProviderSecretTestDoubles.Resolver(_secrets).GetClientSecretAsync(Provider(clientSecretReference: Reference));

            seen.Should().BeSameAs(caller);
            BlocksContext.GetContext().Should().BeSameAs(caller);
        }

        // In an ordinary flow the runtime rolls back AsyncLocal changes made inside an awaited
        // method, so a missing `finally` restore would go unnoticed. Here, on a pool thread, the
        // flow is suppressed and the pending read is completed on that same thread: the resolver's
        // code after its await runs inline in this thread's ExecutionContext with no rollback, so
        // the context left here is exactly what the resolver's explicit restore wrote.
        // `completingContext` is put on the thread first, so the result differs from the caller's
        // context whenever no restore happens.
        private async Task<(BlocksContext? Left, Task<string?> Read)> ContextLeftAfterReadAsync(
            BlocksContext caller, BlocksContext completingContext, Action<TaskCompletionSource<string>> completeRead)
        {
            var read = new TaskCompletionSource<string>(); // inline continuations on purpose
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).Returns(read.Task);
            var sut = IdentityProviderSecretTestDoubles.Resolver(_secrets);

            return await Task.Run(() =>
            {
                BlocksContext.SetContext(caller, false);
                var flow = ExecutionContext.SuppressFlow();
                try
                {
                    var pending = sut.GetClientSecretAsync(Provider(clientSecretReference: Reference));
                    pending.IsCompleted.Should().BeFalse("the secret read must still be pending");

                    BlocksContext.SetContext(completingContext, true);
                    completeRead(read);
                    pending.IsCompleted.Should().BeTrue("the resolver must have finished inline on this thread");

                    return (BlocksContext.GetContext(), pending);
                }
                finally
                {
                    flow.Undo();
                }
            });
        }

        [Theory]
        [InlineData("success")]
        [InlineData(nameof(SecretNotFoundException))]
        [InlineData(nameof(InvalidOperationException))]
        public async Task AfterTheRead_TheCallersContextIsExplicitlyRestored(string outcome)
        {
            // H13: whether the read succeeds or throws, the resolver writes the caller's context back.
            var caller = IdentityProviderSecretTestDoubles.AnonymousContext(TenantId);
            Action<TaskCompletionSource<string>> complete = outcome switch
            {
                "success" => r => r.SetResult(VaultValue),
                nameof(SecretNotFoundException) => r => r.SetException(new SecretNotFoundException(Reference)),
                _ => r => r.SetException(new InvalidOperationException("store misconfigured")),
            };

            var (left, read) = await ContextLeftAfterReadAsync(caller, CallerContext("completing-tenant"), complete);

            left.Should().BeSameAs(caller);
            if (outcome == "success")
                (await read).Should().Be(VaultValue);
            else
                await read.Invoking(t => t).Should().ThrowAsync<IdentityProviderSecretUnavailableException>();
        }

        [Fact]
        public async Task NoAmbientTenant_FailsClosed_WithoutAScope()
        {
            // C9: a defaulted tenant would be worse than not resolving.
            var (sut, scopes) = CountingResolver();

            var act = () => sut.GetClientSecretAsync(Provider(clientSecretReference: Reference));

            var thrown = await act.Should().ThrowAsync<IdentityProviderSecretUnavailableException>();
            thrown.Which.FailureClass.Should().Be(IdentityProviderSecretResolver.NoTenantFailure);
            scopes.Created.Should().Be(0);
        }

        [Fact]
        public async Task ConcurrentReads_EachRunInTheirOwnTenant()
        {
            // C9: tenant A's read never runs in tenant B's store, even for a same-named provider.
            var gateA = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var gateB = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var bothStarted = new CountdownEvent(2);
            var tenants = new ConcurrentDictionary<string, string>();
            _secrets.Setup(s => s.GetValueAsync("ref-a", It.IsAny<CancellationToken>()))
                .Returns(() => { tenants["ref-a"] = BlocksContext.GetContext()!.TenantId; bothStarted.Signal(); return gateA.Task; });
            _secrets.Setup(s => s.GetValueAsync("ref-b", It.IsAny<CancellationToken>()))
                .Returns(() => { tenants["ref-b"] = BlocksContext.GetContext()!.TenantId; bothStarted.Signal(); return gateB.Task; });
            var sut = IdentityProviderSecretTestDoubles.Resolver(_secrets);

            Task<string?> ReadAs(string tenant, string reference) => Task.Run(() =>
            {
                BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(tenant), false);
                return sut.GetClientSecretAsync(Provider(clientSecretReference: reference));
            });

            var a = ReadAs("tenant-a", "ref-a");
            var b = ReadAs("tenant-b", "ref-b");
            bothStarted.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("both reads must be in flight together");
            gateB.SetResult("value-b");
            gateA.SetResult("value-a");

            (await a).Should().Be("value-a");
            (await b).Should().Be("value-b");
            tenants["ref-a"].Should().Be("tenant-a");
            tenants["ref-b"].Should().Be("tenant-b");
        }

        // ---------- failures (C2, C3, C4, C8) ----------

        public static TheoryData<Exception> ReadFailures => new()
        {
            new SecretNotFoundException(Reference),
            new SecretStateException("Locked", "read"),
            new SecretStateException("Deleted", "read"),
            new SecretAccessDeniedException("denied"),
            new SecretVaultException("vault down for " + Reference, "get", Reference),
            new InvalidOperationException("misconfigured " + Reference),
            new TimeoutException("slow"),
        };

        [Theory]
        [MemberData(nameof(ReadFailures))]
        public async Task ReadFailure_ThrowsUnavailable_WithProviderAndFailureClassOnly_AndNoFallback(Exception failure)
        {
            // C2, C3, C4 (no fallback to the legacy field), C8 (no value or reference id)
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ThrowsAsync(failure);
            var logger = new CapturingLogger<IdentityProviderSecretResolver>();
            var sut = IdentityProviderSecretTestDoubles.Resolver(_secrets, logger);

            var act = () => sut.GetClientSecretAsync(Provider(clientSecretReference: Reference));

            var thrown = (await act.Should().ThrowAsync<IdentityProviderSecretUnavailableException>()).Which;
            thrown.ProviderName.Should().Be("google");
            thrown.Kind.Should().Be(IdentityProviderSecretKind.ClientSecret);
            thrown.FailureClass.Should().Be(failure.GetType().Name);
            thrown.InnerException.Should().BeNull("Secret* messages name the reference id");
            thrown.ToString().Should().NotContain(Reference).And.NotContain(LegacyValue);

            var entry = logger.Entries.Should().ContainSingle().Subject;
            entry.Level.Should().Be(LogLevel.Warning);
            entry.AllText.Should().Contain("google").And.Contain(failure.GetType().Name);
            entry.AllText.Should().NotContain(Reference).And.NotContain(LegacyValue).And.NotContain(VaultValue);
        }

        [Fact]
        public async Task SuccessfulRead_LogsNothing()
        {
            // C8: the value is never logged.
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ReturnsAsync(VaultValue);
            var logger = new CapturingLogger<IdentityProviderSecretResolver>();

            await IdentityProviderSecretTestDoubles.Resolver(_secrets, logger).GetClientSecretAsync(Provider(clientSecretReference: Reference));

            logger.Entries.Should().BeEmpty();
        }

        [Fact]
        public async Task Cancellation_Propagates()
        {
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());

            var act = () => IdentityProviderSecretTestDoubles.Resolver(_secrets).GetClientSecretAsync(Provider(clientSecretReference: Reference));

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task SecretServiceNotRegistered_FailsAsUnavailable()
        {
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);
            using var root = new ServiceCollection().BuildServiceProvider();
            var sut = new IdentityProviderSecretResolver(root.GetRequiredService<IServiceScopeFactory>(), NullLogger<IdentityProviderSecretResolver>.Instance);

            var act = () => sut.GetClientSecretAsync(Provider(clientSecretReference: Reference));

            (await act.Should().ThrowAsync<IdentityProviderSecretUnavailableException>()).Which.FailureClass.Should().Be(nameof(InvalidOperationException));
        }

        // ---------- writes (run as the console caller) ----------

        [Fact]
        public async Task Store_RunsAsTheCaller_WithServiceTypeNamesAndDescriptions()
        {
            var caller = CallerContext("project-tenant");
            BlocksContext.SetContext(caller, true);
            BlocksContext? seen = null;
            SetSecretRequest? request = null;
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()))
                .Callback<SetSecretRequest, CancellationToken>((r, _) => { request = r; seen = BlocksContext.GetContext(); })
                .ReturnsAsync("new-ref");

            var refs = await IdentityProviderSecretTestDoubles.Resolver(_secrets).StoreAsync(Provider(), "s", null);

            refs.Should().Be(("new-ref", (string?)null));
            seen.Should().BeSameAs(caller);
            request!.Name.Should().Be(IdentityProviderSecretResolver.ClientSecretName);
            request.Type.Should().Be(SecretTypes.Service);
            request.Description.Should().Be("Identity provider google (tenant project-tenant): client secret");
            request.Description.Should().NotContain("idp-1", "the repository assigns the provider id on insert");
        }

        [Fact]
        public async Task Store_NothingSupplied_OpensNoScope()
        {
            var (sut, scopes) = CountingResolver();

            (await sut.StoreAsync(Provider(), " ", null)).Should().Be(((string?)null, (string?)null));

            scopes.Created.Should().Be(0);
        }

        [Fact]
        public async Task Retire_UnexpectedFailure_LogsAWarningWithoutTheReference()
        {
            // C12, C8
            _secrets.Setup(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>())).ThrowsAsync(new SecretVaultException("down " + Reference, "delete", Reference));
            var logger = new CapturingLogger<IdentityProviderSecretResolver>();

            await IdentityProviderSecretTestDoubles.Resolver(_secrets, logger).RetireAsync(Provider(), IdentityProviderSecretKind.PrivateKey, Reference);

            var entry = logger.Entries.Should().ContainSingle().Subject;
            entry.Level.Should().Be(LogLevel.Warning);
            entry.AllText.Should().Contain("google").And.Contain(nameof(SecretVaultException)).And.NotContain(Reference);
        }
    }

    /// <summary>
    /// The ambient-HTTP-identity cases. They switch <see cref="BlocksContext.IsTestMode"/> off, which
    /// is process-wide, so they run outside the parallel test collections.
    /// </summary>
    [Collection(nameof(IdentityProviderSecretHttpIdentityCollection))]
    public sealed class IdentityProviderSecretResolverHttpIdentityTests
    {
        private const string Reference = "ref-http";

        private static async Task<BlocksContext?> ReadUnderHttpIdentityAsync(ClaimsIdentity identity, BlocksContext? asyncLocal)
        {
            BlocksContext? seen = null;
            var secrets = new Mock<ISecretService>(MockBehavior.Strict);
            secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()))
                .Callback(() => seen = BlocksContext.GetContext())
                .ReturnsAsync("v");
            var sut = IdentityProviderSecretTestDoubles.Resolver(secrets);

            var previousMode = BlocksContext.IsTestMode;
            var previousAccessor = BlocksHttpContextAccessor.Instance;
            var accessor = new HttpContextAccessor();
            BlocksContext.IsTestMode = false;
            BlocksHttpContextAccessor.Instance = accessor;
            try
            {
                accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
                BlocksContext.SetContext(asyncLocal, false);

                (await Task.Run(() => sut.GetClientSecretAsync(new IdentityProvider
                {
                    Provider = "google", ProviderType = "social", ClientId = "cid", ClientSecret = "", ClientSecretReference = Reference,
                    TokenEndpointAuthMethod = "client_secret_post",
                }))).Should().Be("v");

                return seen;
            }
            finally
            {
                accessor.HttpContext = null;
                BlocksHttpContextAccessor.Instance = previousAccessor;
                BlocksContext.SetContext(null, false);
                BlocksContext.IsTestMode = previousMode;
            }
        }

        [Fact]
        public async Task UnauthenticatedHttpIdentity_ReadsAsTheSynthesizedServiceActor()
        {
            // H13 on a real anonymous request: the HTTP identity exists but is not authenticated.
            var seen = await ReadUnderHttpIdentityAsync(new ClaimsIdentity(), IdentityProviderSecretTestDoubles.AnonymousContext("tenant-a"));

            seen!.IsAuthenticated.Should().BeTrue();
            seen.TenantId.Should().Be("tenant-a");
            seen.UserId.Should().Be(IdentityProviderSecretResolver.ServiceUserId);
        }

        [Fact]
        public async Task AuthenticatedHttpIdentity_IsTheAuditActor()
        {
            // H13: a real authenticated caller wins over the synthesized actor.
            var identity = new ClaimsIdentity(
                new[] { new Claim("tenant_id", "caller-tenant"), new Claim("user_id", "caller-user"), new Claim("sub", "caller-user") },
                "Bearer");

            var seen = await ReadUnderHttpIdentityAsync(identity, null);

            seen!.TenantId.Should().Be("caller-tenant");
            seen.UserId.Should().NotBe(IdentityProviderSecretResolver.ServiceUserId);
        }
    }

    [CollectionDefinition(nameof(IdentityProviderSecretHttpIdentityCollection), DisableParallelization = true)]
    public sealed class IdentityProviderSecretHttpIdentityCollection { }
}
