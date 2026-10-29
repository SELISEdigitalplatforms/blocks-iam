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
    /// <see cref="ClientCredentialSecretResolver"/>: the reference rule, the context a token-request
    /// read runs under, the failure class, the secret it writes, and what reaches the logs.
    /// </summary>
    public sealed class ClientCredentialSecretResolverTests : IDisposable
    {
        private const string TenantId = "tenant-a";
        private const string Reference = "ref-5d2c";
        private const string VaultValue = "blxk_vault-value";
        private const string LegacyValue = "blxk_legacy-plaintext";

        private readonly Mock<ISecretService> _secrets = new(MockBehavior.Strict);

        public ClientCredentialSecretResolverTests()
        {
            BlocksContext.IsTestMode = true;
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null, false);
            BlocksContext.IsTestMode = false;
        }

        private static ClientCredential Credential(string? reference = null) => new()
        {
            ItemId = "cc-1",
            Name = "runner",
            OrganizationId = "org-7",
            ClientSecret = reference == null ? LegacyValue : string.Empty,
            ClientSecretReference = reference,
        };

        // ---------- reference rule ----------

        [Fact]
        public async Task LegacyCredential_ReturnsThePlaintext_WithoutASecretCall()
        {
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);

            (await ClientCredentialSecretTestDoubles.Resolver(_secrets).GetSecretAsync(Credential())).Should().Be(LegacyValue);

            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ReferencedCredential_IsReadFromTheStore_AndThePlaintextFieldIsIgnored()
        {
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ReturnsAsync(VaultValue);
            var credential = Credential(Reference);
            credential.ClientSecret = LegacyValue;

            (await ClientCredentialSecretTestDoubles.Resolver(_secrets).GetSecretAsync(credential)).Should().Be(VaultValue);

            _secrets.Verify(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EveryCall_ReadsTheStoreAgain()
        {
            // No cache: one store read per token request.
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ReturnsAsync(VaultValue);
            var sut = ClientCredentialSecretTestDoubles.Resolver(_secrets);

            await sut.GetSecretAsync(Credential(Reference));
            await sut.GetSecretAsync(Credential(Reference));

            _secrets.Verify(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public void Resolver_IsASingletonThatDoesNotCaptureTheScopedSecretService()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped(_ => _secrets.Object);
            services.AddSingleton<IClientCredentialSecretResolver, ClientCredentialSecretResolver>();
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

            provider.GetRequiredService<IClientCredentialSecretResolver>()
                .Should().BeSameAs(provider.GetRequiredService<IClientCredentialSecretResolver>());
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

            await ClientCredentialSecretTestDoubles.Resolver(_secrets).GetSecretAsync(Credential(Reference));

            seen.Should().NotBeNull();
            seen!.IsAuthenticated.Should().BeTrue();
            seen.TenantId.Should().Be(TenantId);
            seen.OriginalTenantId.Should().Be(TenantId);
            seen.UserId.Should().Be(ClientCredentialSecretResolver.ServiceUserId);
            BlocksContext.GetContext().Should().BeSameAs(ambient);
        }

        [Fact]
        public async Task AuthenticatedCaller_ReadsAsItself()
        {
            var caller = BlocksContext.Create(TenantId, [], "caller-user", true, "/api/oidc/token", "org", DateTime.UtcNow.AddMinutes(5),
                "caller@test.com", [], "caller", "", "Caller", "token", TenantId);
            BlocksContext.SetContext(caller, true);
            BlocksContext? seen = null;
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()))
                .Callback(() => seen = BlocksContext.GetContext())
                .ReturnsAsync(VaultValue);

            await ClientCredentialSecretTestDoubles.Resolver(_secrets).GetSecretAsync(Credential(Reference));

            seen.Should().BeSameAs(caller);
        }

        // ---------- failures ----------

        [Fact]
        public async Task ReferencedCredential_WithNoAmbientTenant_FailsClosed_WithoutASecretCall()
        {
            var act = () => ClientCredentialSecretTestDoubles.Resolver(_secrets).GetSecretAsync(Credential(Reference));

            (await act.Should().ThrowAsync<ClientCredentialSecretUnavailableException>())
                .Which.FailureClass.Should().Be(ClientCredentialSecretResolver.NoTenantFailure);
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
            var logger = new CapturingLogger<ClientCredentialSecretResolver>();
            var credential = Credential(Reference);
            credential.ClientSecret = LegacyValue;

            var act = () => ClientCredentialSecretTestDoubles.Resolver(_secrets, logger).GetSecretAsync(credential);

            var ex = (await act.Should().ThrowAsync<ClientCredentialSecretUnavailableException>()).Which;
            ex.FailureClass.Should().Be(failure.Name);
            ex.ClientId.Should().Be("cc-1");
            ex.Message.Should().NotContain(Reference);
            ex.InnerException.Should().BeNull();
            logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);
            logger.Entries.Should().OnlyContain(e => !e.AllText.Contains(Reference) && !e.AllText.Contains(LegacyValue));
        }

        // ---------- writes ----------

        [Fact]
        public async Task Store_WritesAServiceSecret_UnderTheCredentialsOrganization()
        {
            BlocksContext.SetContext(IdentityProviderSecretTestDoubles.AnonymousContext(TenantId), false);
            SetSecretRequest? sent = null;
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()))
                .Callback<SetSecretRequest, CancellationToken>((r, _) => sent = r)
                .ReturnsAsync(Reference);

            var reference = await ClientCredentialSecretTestDoubles.Resolver(_secrets).StoreAsync(Credential(), VaultValue);

            reference.Should().Be(Reference);
            sent!.Name.Should().Be(ClientCredentialSecretResolver.SecretName);
            sent.Type.Should().Be(SecretTypes.Service);
            sent.Value.Should().Be(VaultValue);
            sent.OrganizationId.Should().Be("org-7");
            sent.Description.Should().Contain("runner").And.Contain("cc-1").And.Contain(TenantId).And.NotContain(VaultValue);
        }

        [Fact]
        public async Task Replace_WithAReference_RotatesInPlace()
        {
            _secrets.Setup(s => s.RotateAsync(Reference, It.Is<RotateSecretRequest>(r => r.Value == VaultValue), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var (reference, created) = await ClientCredentialSecretTestDoubles.Resolver(_secrets).ReplaceAsync(Credential(Reference), VaultValue);

            reference.Should().Be(Reference);
            created.Should().BeFalse();
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Replace_WithoutAReference_StoresANewSecret()
        {
            _secrets.Setup(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Value == VaultValue), It.IsAny<CancellationToken>())).ReturnsAsync("ref-new");

            var (reference, created) = await ClientCredentialSecretTestDoubles.Resolver(_secrets).ReplaceAsync(Credential(), VaultValue);

            reference.Should().Be("ref-new");
            created.Should().BeTrue();
            _secrets.Verify(s => s.RotateAsync(It.IsAny<string>(), It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Replace_WhenTheReferencedSecretIsGoneOrDeleted_StoresANewSecret(bool notFound)
        {
            Exception gone = notFound ? new SecretNotFoundException(Reference) : new SecretStateException("deleted", "rotate");
            _secrets.Setup(s => s.RotateAsync(Reference, It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(gone);
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync("ref-new");

            var (reference, created) = await ClientCredentialSecretTestDoubles.Resolver(_secrets).ReplaceAsync(Credential(Reference), VaultValue);

            reference.Should().Be("ref-new");
            created.Should().BeTrue();
        }

        [Fact]
        public async Task Replace_WhenTheStoreIsDown_Throws()
        {
            _secrets.Setup(s => s.RotateAsync(Reference, It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecretVaultException("down", "rotate", Reference));

            var act = () => ClientCredentialSecretTestDoubles.Resolver(_secrets).ReplaceAsync(Credential(Reference), VaultValue);

            await act.Should().ThrowAsync<SecretVaultException>();
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Retire_DeletesTheSecret()
        {
            _secrets.Setup(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            await ClientCredentialSecretTestDoubles.Resolver(_secrets).RetireAsync(Credential(Reference), Reference);

            _secrets.Verify(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task Retire_WithoutAReference_DoesNothing(string? reference)
        {
            await ClientCredentialSecretTestDoubles.Resolver(_secrets).RetireAsync(Credential(), reference);

            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Retire_IgnoresAnAlreadyGoneSecret_AndLogsOtherFailuresByTypeOnly()
        {
            var logger = new CapturingLogger<ClientCredentialSecretResolver>();
            _secrets.Setup(s => s.DeleteAsync("gone", It.IsAny<CancellationToken>())).ThrowsAsync(new SecretNotFoundException("gone"));
            _secrets.Setup(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>())).ThrowsAsync(new SecretVaultException("down " + Reference, "delete", Reference));
            var sut = ClientCredentialSecretTestDoubles.Resolver(_secrets, logger);

            await sut.RetireAsync(Credential(), "gone");
            await sut.RetireAsync(Credential(), Reference);

            logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning)
                .Which.AllText.Should().Contain(nameof(SecretVaultException)).And.NotContain(Reference);
        }
    }
}
