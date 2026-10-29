using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.RequestModel;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Blocks.Secrets;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net.Http;
using XUnitTest.TestDoubles;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// OIDC client secrets in Blocks Secrets: create stores the secret and keeps a reference, an
    /// edit moves a plaintext secret unchanged, a switch to device flow drops it, rotate replaces
    /// it, delete retires it (once, also when a linked provider shares it), and a write that does
    /// not land leaves no orphan and no dangling reference.
    /// </summary>
    public sealed class OidcClientSecretTests : IDisposable
    {
        private const string Reference = "oidc-ref-1";
        private const string LegacyValue = "blxsk_legacy-plaintext";
        private const string StoreUnavailable = "secret_store_unavailable";

        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<ISecretService> _secrets = new(MockBehavior.Strict);
        private readonly Mock<IValidator<SaveOIDCClientRequest>> _validator = new();
        private readonly CapturingLogger<AuthenticationDomainService> _logger = new();

        public OidcClientSecretTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
            _validator.Setup(v => v.ValidateAsync(It.IsAny<SaveOIDCClientRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private AuthenticationDomainService Create() =>
            new(Mock.Of<IMessageClient>(), _repo.Object,
                new AuthenticationRequestValidators(
                    _validator.Object, Mock.Of<IValidator<SaveOidcUiTemplateRequest>>(),
                    Mock.Of<IValidator<SaveIdentityProviderRequest>>(), Mock.Of<IValidator<UpdateIdentityProviderRequest>>()),
                Mock.Of<IHttpClientFactory>(), IdentityProviderSecretTestDoubles.Resolver(_secrets),
                ClientCredentialSecretTestDoubles.Resolver(_secrets), OidcClientSecretTestDoubles.Resolver(_secrets), _logger);

        private static SaveOIDCClientRequest Request(string? itemId = null, bool deviceFlow = false) => new()
        {
            ItemId = itemId,
            RedirectUris = deviceFlow ? new() : new() { "https://app/cb" },
            AllowedScopes = new() { "openid" },
            ClientType = deviceFlow ? "public" : "confidential",
            ClientDisplayName = "App",
            IsDeviceFlowClient = deviceFlow,
        };

        private static OidcClientRegistration Client(string? reference = null) => new()
        {
            ItemId = "c1",
            ClientId = "c1",
            ClientSecret = reference == null ? LegacyValue : string.Empty,
            ClientSecretReference = reference,
        };

        private void StoreAccepts() =>
            _secrets.Setup(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Name == OidcClientSecretResolver.SecretName), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Reference);

        private void DeleteAccepts() =>
            _secrets.Setup(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // ---------- save ----------

        [Fact]
        public async Task Create_WhenTheStoreIsDown_ReturnsSecretStoreUnavailable_AndSavesNothing()
        {
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecretVaultException("down", "set", null));

            var result = await Create().SaveOIDCClientAsync(Request());

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey(StoreUnavailable);
            _repo.Verify(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()), Times.Never);
        }

        [Fact]
        public async Task Create_WhenTheSaveThrows_RetiresTheNewSecret()
        {
            StoreAccepts();
            DeleteAccepts();
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>())).ThrowsAsync(new TimeoutException());

            var act = () => Create().SaveOIDCClientAsync(Request());

            await act.Should().ThrowAsync<TimeoutException>();
            _secrets.Verify(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Edit_WhenTheSaveThrows_AndTheClientCannotBeReadBack_KeepsTheMovedSecret()
        {
            // Orphan over dangling: the update may have landed, so its new reference is not retired blind.
            StoreAccepts();
            _repo.SetupSequence(r => r.GetOidcClientRegistrationAsync("c1"))
                .ReturnsAsync(Client())
                .ReturnsAsync((OidcClientRegistration)null!);
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>())).ThrowsAsync(new TimeoutException());

            var act = () => Create().SaveOIDCClientAsync(Request("c1"));

            await act.Should().ThrowAsync<TimeoutException>();
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Edit_ReferencedClient_MakesNoSecretCall()
        {
            var client = Client(Reference);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(client);
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>())).Returns(Task.CompletedTask);

            var result = await Create().SaveOIDCClientAsync(Request("c1"));

            result.IsSuccess.Should().BeTrue();
            client.ClientSecretReference.Should().Be(Reference);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SwitchingToDeviceFlow_DropsTheSecret_AndRetiresItAfterTheSave()
        {
            var client = Client(Reference);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(client);
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>())).Returns(Task.CompletedTask);
            DeleteAccepts();

            var result = await Create().SaveOIDCClientAsync(Request("c1", deviceFlow: true));

            result.IsSuccess.Should().BeTrue();
            client.ClientSecret.Should().BeNull();
            client.ClientSecretReference.Should().BeNull();
            _secrets.Verify(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---------- rotate ----------

        [Fact]
        public async Task Rotate_WhenTheStoreIsDown_ReturnsSecretStoreUnavailable_AndSavesNothing()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(Client(Reference));
            _secrets.Setup(s => s.RotateAsync(Reference, It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecretVaultException("down", "rotate", Reference));

            var result = await Create().RotateOidcClientSecretAsync("c1");

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey(StoreUnavailable);
            result.ClientSecret.Should().BeNull();
            _repo.Verify(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()), Times.Never);
        }

        [Fact]
        public async Task Rotate_NewSecret_WhenTheSaveThrows_RetiresIt()
        {
            StoreAccepts();
            DeleteAccepts();
            _repo.SetupSequence(r => r.GetOidcClientRegistrationAsync("c1"))
                .ReturnsAsync(Client())
                .ReturnsAsync(Client());
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>())).ThrowsAsync(new TimeoutException());

            var act = () => Create().RotateOidcClientSecretAsync("c1");

            await act.Should().ThrowAsync<TimeoutException>();
            _secrets.Verify(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Rotate_InPlace_WhenTheSaveThrows_LogsThatItCannotBeUndone()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(Client(Reference));
            _secrets.Setup(s => s.RotateAsync(Reference, It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>())).ThrowsAsync(new TimeoutException());

            var act = () => Create().RotateOidcClientSecretAsync("c1");

            await act.Should().ThrowAsync<TimeoutException>();
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.AllText.Contains("cannot be undone"));
        }

        // ---------- delete ----------

        [Fact]
        public async Task Delete_RetiresTheSecret_OnceTheClientIsGone()
        {
            _repo.SetupSequence(r => r.GetOidcClientRegistrationAsync("c1"))
                .ReturnsAsync(Client(Reference))
                .ReturnsAsync((OidcClientRegistration)null!);
            _repo.Setup(r => r.GetIdentityProvidersAsync()).ReturnsAsync(new List<IdentityProvider>());
            DeleteAccepts();

            (await Create().DeleteOidcClientAsync(new DeleteOIDCClientRequest { ItemId = "c1" })).IsSuccess.Should().BeTrue();

            _secrets.Verify(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Delete_Denied_KeepsTheSecret()
        {
            // A denied delete is a silent no-op: the client is still there, so its secret is kept.
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(Client(Reference));
            _repo.Setup(r => r.GetIdentityProvidersAsync()).ReturnsAsync(new List<IdentityProvider>());

            (await Create().DeleteOidcClientAsync(new DeleteOIDCClientRequest { ItemId = "c1" })).IsSuccess.Should().BeTrue();

            _secrets.VerifyNoOtherCalls();
        }

        private static IdentityProvider LinkedProvider() => new()
        {
            ItemId = "idp-linked", Provider = "app", ProviderType = "blocks-oidc", Protocol = "oidc", ClientId = "c1", TokenEndpointAuthMethod = "client_secret_post",
            ClientSecret = string.Empty, ClientSecretReference = Reference,
        };

        [Fact]
        public async Task Delete_WithALinkedProviderSharingTheSecret_RetiresItOnce()
        {
            _repo.SetupSequence(r => r.GetOidcClientRegistrationAsync("c1"))
                .ReturnsAsync(Client(Reference))
                .ReturnsAsync((OidcClientRegistration)null!);
            _repo.Setup(r => r.GetIdentityProvidersAsync()).ReturnsAsync(new List<IdentityProvider> { LinkedProvider() });
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-linked")).ReturnsAsync((IdentityProvider)null!);
            DeleteAccepts();

            (await Create().DeleteOidcClientAsync(new DeleteOIDCClientRequest { ItemId = "c1" })).IsSuccess.Should().BeTrue();

            _repo.Verify(r => r.DeleteIdentityProviderAsync("idp-linked"), Times.Once);
            _secrets.Verify(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteIdentityProvider_DeletesItsOidcClient_AndRetiresTheSharedSecretOnce()
        {
            // The provider goes first: its shared secret still belongs to the live client and is kept,
            // then the client goes and its secret is retired.
            _repo.SetupSequence(r => r.GetIdentityProviderByIdAsync("idp-linked"))
                .ReturnsAsync(LinkedProvider())
                .ReturnsAsync((IdentityProvider)null!);
            _repo.SetupSequence(r => r.GetOidcClientRegistrationAsync("c1"))
                .ReturnsAsync(Client(Reference))
                .ReturnsAsync(Client(Reference))
                .ReturnsAsync((OidcClientRegistration)null!);
            DeleteAccepts();

            (await Create().DeleteIdentityProviderAsync("idp-linked")).IsSuccess.Should().BeTrue();

            _repo.Verify(r => r.DeleteOidcClientAsync(It.Is<DeleteOIDCClientRequest>(d => d.ItemId == "c1")), Times.Once);
            _secrets.Verify(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---------- reads ----------

        [Fact]
        public async Task GetClients_NeverReturnTheSecretOrItsReference()
        {
            _repo.Setup(r => r.GetOIDCCredentialsByTenantForViewAsync()).ReturnsAsync(new List<OidcClientRegistration> { Client(Reference), Client() });

            var result = await Create().GetOidcClientsAsync();

            result.oIDCClientCredentials.Should().OnlyContain(c => c.ClientSecret == null && c.ClientSecretReference == null);
        }
    }
}
