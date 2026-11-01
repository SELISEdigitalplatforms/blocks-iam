using Authentication.DomainService.OAuth;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Blocks.Secrets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using XUnitTest.TestDoubles;

namespace XUnitTest.Auth.OAuth
{
    /// <summary>
    /// <see cref="OidcClientSecretResolver"/>: the reference rule, the context a token-endpoint read
    /// runs under, the failure class, the secret it writes, what reaches the logs, and the shared
    /// <see cref="OidcClientSecretResolverExtensions.CheckSecretAsync"/>.
    /// </summary>
    public sealed class OidcClientSecretResolverTests : IDisposable
    {
        private const string TenantId = "tenant-a";
        private const string Reference = "ref-9a1e";
        private const string VaultValue = "blxsk_vault-value";
        private const string LegacyValue = "blxsk_legacy-plaintext";

        private readonly Mock<ISecretService> _secrets = new(MockBehavior.Strict);

        public OidcClientSecretResolverTests()
        {
            BlocksContext.IsTestMode = true;
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null, false);
            BlocksContext.IsTestMode = false;
        }

        private static OidcClientRegistration Client(string? reference = null) => new()
        {
            ItemId = "oc-1",
            ClientId = "oc-1",
            ClientName = "portal",
            OrganizationId = "org-3",
            ClientSecret = reference == null ? LegacyValue : string.Empty,
            ClientSecretReference = reference,
        };

        // ---------- reference rule ----------

        [Fact]
        public async Task LegacyClient_ReturnsThePlaintext_WithoutASecretCall()
        {
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);

            (await OidcClientSecretTestDoubles.Resolver(_secrets).GetSecretAsync(Client())).Should().Be(LegacyValue);

            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ReferencedClient_IsReadFromTheStore_AndThePlaintextFieldIsIgnored()
        {
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ReturnsAsync(VaultValue);
            var client = Client(Reference);
            client.ClientSecret = LegacyValue;

            (await OidcClientSecretTestDoubles.Resolver(_secrets).GetSecretAsync(client)).Should().Be(VaultValue);

            _secrets.Verify(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void Resolver_IsASingletonThatDoesNotCaptureTheScopedSecretService()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped(_ => _secrets.Object);
            services.AddSingleton<IOidcClientSecretResolver, OidcClientSecretResolver>();
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

            provider.GetRequiredService<IOidcClientSecretResolver>()
                .Should().BeSameAs(provider.GetRequiredService<IOidcClientSecretResolver>());
        }

        // ---------- context ----------

        [Fact]
        public async Task AnonymousTokenRequest_ReadsInAServiceContextForTheAmbientTenant_AndRestoresIt()
        {
            var ambient = IdentityProviderSecretTestDoubles.AnonymousContext(TenantId);
            BlocksContext.SetContext(ambient, false);
            BlocksContext? seen = null;
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()))
                .Callback(() => seen = BlocksContext.GetContext())
                .ReturnsAsync(VaultValue);

            await OidcClientSecretTestDoubles.Resolver(_secrets).GetSecretAsync(Client(Reference));

            seen.Should().NotBeNull();
            seen!.IsAuthenticated.Should().BeTrue();
            seen.TenantId.Should().Be(TenantId);
            seen.UserId.Should().Be(OidcClientSecretResolver.ServiceUserId);
            BlocksContext.GetContext().Should().BeSameAs(ambient);
        }

        // ---------- failures ----------

        [Fact]
        public async Task ReferencedClient_WithNoAmbientTenant_FailsClosed_WithoutASecretCall()
        {
            var act = () => OidcClientSecretTestDoubles.Resolver(_secrets).GetSecretAsync(Client(Reference));

            (await act.Should().ThrowAsync<OidcClientSecretUnavailableException>())
                .Which.FailureClass.Should().Be(OidcClientSecretResolver.NoTenantFailure);
            _secrets.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(typeof(SecretVaultException))]
        [InlineData(typeof(SecretNotFoundException))]
        [InlineData(typeof(SecretStateException))]
        public async Task StoreFailure_FailsClosed_WithTheTypeOnly_AndNeverFallsBackToThePlaintext(Type failure)
        {
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);
            Exception thrown = failure == typeof(SecretVaultException) ? new SecretVaultException("down " + Reference, "get", Reference)
                : failure == typeof(SecretNotFoundException) ? new SecretNotFoundException(Reference)
                : new SecretStateException("locked", "read");
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ThrowsAsync(thrown);
            var logger = new CapturingLogger<OidcClientSecretResolver>();
            var client = Client(Reference);
            client.ClientSecret = LegacyValue;

            var act = () => OidcClientSecretTestDoubles.Resolver(_secrets, logger).GetSecretAsync(client);

            var ex = (await act.Should().ThrowAsync<OidcClientSecretUnavailableException>()).Which;
            ex.FailureClass.Should().Be(failure.Name);
            ex.ClientId.Should().Be("oc-1");
            ex.Message.Should().NotContain(Reference);
            ex.InnerException.Should().BeNull();
            logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);
            logger.Entries.Should().OnlyContain(e => !e.AllText.Contains(Reference) && !e.AllText.Contains(LegacyValue));
        }

        // ---------- CheckSecretAsync ----------

        [Theory]
        [InlineData(VaultValue, OidcClientSecretCheck.Matches)]
        [InlineData("wrong", OidcClientSecretCheck.DoesNotMatch)]
        [InlineData("", OidcClientSecretCheck.DoesNotMatch)]
        [InlineData(null, OidcClientSecretCheck.DoesNotMatch)]
        public async Task Check_ComparesThePresentedSecretWithTheStoredOne(string? presented, OidcClientSecretCheck expected)
        {
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ReturnsAsync(VaultValue);

            (await OidcClientSecretTestDoubles.Resolver(_secrets).CheckSecretAsync(Client(Reference), presented)).Should().Be(expected);
        }

        [Fact]
        public async Task Check_AClientWithoutASecret_NeverMatches()
        {
            var client = Client();
            client.ClientSecret = null;

            (await OidcClientSecretTestDoubles.Resolver(_secrets).CheckSecretAsync(client, "")).Should().Be(OidcClientSecretCheck.DoesNotMatch);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Check_AnUnreadableSecret_IsUnavailable_NotAMismatch()
        {
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ThrowsAsync(new SecretVaultException("down", "get", Reference));

            (await OidcClientSecretTestDoubles.Resolver(_secrets).CheckSecretAsync(Client(Reference), VaultValue))
                .Should().Be(OidcClientSecretCheck.Unavailable);
        }

        // ---------- writes ----------

        [Fact]
        public async Task Store_WritesAServiceSecret_UnderTheClientsOrganization()
        {
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);
            SetSecretRequest? sent = null;
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()))
                .Callback<SetSecretRequest, CancellationToken>((r, _) => sent = r)
                .ReturnsAsync(Reference);

            var reference = await OidcClientSecretTestDoubles.Resolver(_secrets).StoreAsync(Client(), VaultValue);

            reference.Should().Be(Reference);
            sent!.Name.Should().Be(OidcClientSecretResolver.SecretName);
            sent.Type.Should().Be(SecretTypes.Service);
            sent.Value.Should().Be(VaultValue);
            sent.OrganizationId.Should().Be("org-3");
            sent.Description.Should().Contain("portal").And.Contain("oc-1").And.Contain(TenantId).And.NotContain(VaultValue);
        }

        [Fact]
        public async Task Replace_WithAReference_RotatesInPlace()
        {
            _secrets.Setup(s => s.RotateAsync(Reference, It.Is<RotateSecretRequest>(r => r.Value == VaultValue), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var (reference, created) = await OidcClientSecretTestDoubles.Resolver(_secrets).ReplaceAsync(Client(Reference), VaultValue);

            reference.Should().Be(Reference);
            created.Should().BeFalse();
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Replace_WhenTheReferencedSecretIsGoneOrDeleted_StoresANewSecret(bool notFound)
        {
            Exception gone = notFound ? new SecretNotFoundException(Reference) : new SecretStateException("deleted", "rotate");
            _secrets.Setup(s => s.RotateAsync(Reference, It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(gone);
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync("ref-new");

            var (reference, created) = await OidcClientSecretTestDoubles.Resolver(_secrets).ReplaceAsync(Client(Reference), VaultValue);

            reference.Should().Be("ref-new");
            created.Should().BeTrue();
        }

        [Fact]
        public async Task Retire_IgnoresAnAlreadyGoneSecret_AndLogsOtherFailuresByTypeOnly()
        {
            var logger = new CapturingLogger<OidcClientSecretResolver>();
            _secrets.Setup(s => s.DeleteAsync("gone", It.IsAny<CancellationToken>())).ThrowsAsync(new SecretNotFoundException("gone"));
            _secrets.Setup(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>())).ThrowsAsync(new SecretVaultException("down " + Reference, "delete", Reference));
            var sut = OidcClientSecretTestDoubles.Resolver(_secrets, logger);

            await sut.RetireAsync(Client(), "gone");
            await sut.RetireAsync(Client(), Reference);

            logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning)
                .Which.AllText.Should().Contain(nameof(SecretVaultException)).And.NotContain(Reference);
        }
    }
}
