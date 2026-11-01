using Authentication.DomainService.Entities;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.OAuth;
using Blocks.Genesis;
using Blocks.Secrets;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net.Http;
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.Auth.Shared
{
    public class AuthenticationDomainServiceTests : IDisposable
    {
        private readonly Mock<IMessageClient> _message = new();
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IValidator<SaveOIDCClientRequest>> _oidcValidator = new();
        private readonly Mock<IValidator<SaveOidcUiTemplateRequest>> _oidcUiTemplateValidator = new();
        private readonly Mock<IValidator<SaveIdentityProviderRequest>> _saveIdpValidator = new();
        private readonly Mock<IValidator<UpdateIdentityProviderRequest>> _updateIdpValidator = new();
        private readonly Mock<IHttpClientFactory> _httpFactory = new();
        private readonly Mock<ISecretService> _secrets = new(MockBehavior.Strict);

        public AuthenticationDomainServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
            _oidcValidator.Setup(v => v.ValidateAsync(It.IsAny<SaveOIDCClientRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
            _oidcUiTemplateValidator.Setup(v => v.ValidateAsync(It.IsAny<SaveOidcUiTemplateRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
            _saveIdpValidator.Setup(v => v.ValidateAsync(It.IsAny<SaveIdentityProviderRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
            _updateIdpValidator.Setup(v => v.ValidateAsync(It.IsAny<UpdateIdentityProviderRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        /// <summary>Re-mint the ambient context with a different organization claim.</summary>
        private static void SetOrganization(string organizationId) =>
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: organizationId,
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));

        private AuthenticationDomainService Create() =>
            new(_message.Object, _repo.Object,
                new AuthenticationRequestValidators(_oidcValidator.Object, _oidcUiTemplateValidator.Object, _saveIdpValidator.Object,
                    _updateIdpValidator.Object), _httpFactory.Object, TestDoubles.IdentityProviderSecretTestDoubles.Resolver(_secrets), TestDoubles.ClientCredentialSecretTestDoubles.Resolver(_secrets), TestDoubles.OidcClientSecretTestDoubles.Resolver(_secrets));

        private static IdentityProvider Idp(string provider = "google", string clientId = "cid", string id = "idp-1") => new()
        {
            ItemId = id, Provider = provider, ProviderType = "social", ClientId = clientId,
            ClientSecret = "secret", TokenEndpointAuthMethod = "client_secret_post", Protocol = "oidc"
        };

        private static SaveIdentityProviderRequest SaveReq(string provider = "google", string clientId = "cid") => new()
        {
            Provider = provider, ProviderType = "social", ClientId = clientId, DisplayName = "Google"
        };

        // ---------- CreateIdentityProviderAsync ----------

        [Fact]
        public async Task CreateIdp_ValidationFails_ReturnsErrors()
        {
            _saveIdpValidator.Setup(v => v.ValidateAsync(It.IsAny<SaveIdentityProviderRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Provider", "required") }));
            var result = await Create().CreateIdentityProviderAsync(SaveReq());
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Provider");
        }

        [Fact]
        public async Task CreateIdp_DuplicateProvider_ReturnsError()
        {
            _repo.Setup(r => r.GetIdentityProviderAsync("google")).ReturnsAsync(Idp());
            var result = await Create().CreateIdentityProviderAsync(SaveReq());
            result.Errors.Should().ContainKey("duplicate_provider");
        }

        [Fact]
        public async Task CreateIdp_DuplicateClientId_ReturnsError()
        {
            _repo.Setup(r => r.GetIdentityProviderAsync("google")).ReturnsAsync((IdentityProvider)null!);
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync("cid")).ReturnsAsync(Idp());
            var result = await Create().CreateIdentityProviderAsync(SaveReq());
            result.Errors.Should().ContainKey("duplicate_client_id");
        }

        [Fact]
        public async Task CreateIdp_HappyPath_Creates()
        {
            _repo.Setup(r => r.GetIdentityProviderAsync("google")).ReturnsAsync((IdentityProvider)null!);
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync("cid")).ReturnsAsync((IdentityProvider)null!);
            _repo.Setup(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(Idp());
            var result = await Create().CreateIdentityProviderAsync(SaveReq());
            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.CreateIdentityProviderAsync(It.Is<IdentityProvider>(p => p.Provider == "google" && p.ClientId == "cid")), Times.Once);
        }

        // ---------- UpdateIdentityProviderAsync ----------

        [Fact]
        public async Task UpdateIdp_EmptyId_ReturnsError()
        {
            var result = await Create().UpdateIdentityProviderAsync("", new UpdateIdentityProviderRequest());
            result.Errors.Should().ContainKey("id_required");
        }

        [Fact]
        public async Task UpdateIdp_NotFound_ReturnsError()
        {
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync((IdentityProvider)null!);
            _repo.Setup(r => r.GetIdentityProviderByIdForViewAsync("idp-1")).ReturnsAsync((IdentityProvider)null!);
            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest());
            result.Errors.Should().ContainKey("not_found");
        }

        [Fact]
        public async Task UpdateIdp_ImmutableProvider_ReturnsError()
        {
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(Idp());
            _repo.Setup(r => r.GetIdentityProviderByIdForViewAsync("idp-1")).ReturnsAsync(Idp());
            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { Provider = "other" });
            result.Errors.Should().ContainKey("immutable_field");
        }

        [Fact]
        public async Task UpdateIdp_ImmutableClientId_ReturnsError()
        {
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(Idp());
            _repo.Setup(r => r.GetIdentityProviderByIdForViewAsync("idp-1")).ReturnsAsync(Idp());
            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientId = "different" });
            result.Errors.Should().ContainKey("immutable_field");
        }

        [Fact]
        public async Task UpdateIdp_HappyPath_PartialMerge()
        {
            var existing = Idp();
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.GetIdentityProviderByIdForViewAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(existing);
            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { DisplayName = "New Name", IsActive = false });
            result.IsSuccess.Should().BeTrue();
            existing.DisplayName.Should().Be("New Name");
            existing.IsActive.Should().BeFalse();
        }

        [Fact]
        public async Task UpdateIdp_ValidationFails_ReturnsErrors()
        {
            _updateIdpValidator.Setup(v => v.ValidateAsync(It.IsAny<UpdateIdentityProviderRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("DisplayName", "required") }));
            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest());
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("DisplayName");
        }

        [Fact]
        public async Task UpdateIdp_ImmutableProviderType_ReturnsError()
        {
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(Idp());
            _repo.Setup(r => r.GetIdentityProviderByIdForViewAsync("idp-1")).ReturnsAsync(Idp());
            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ProviderType = "enterprise" });
            result.Errors.Should().ContainKey("immutable_field");
        }

        [Fact]
        public async Task UpdateIdp_ImmutableProtocol_ReturnsError()
        {
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(Idp());
            _repo.Setup(r => r.GetIdentityProviderByIdForViewAsync("idp-1")).ReturnsAsync(Idp());
            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { Protocol = "saml" });
            result.Errors.Should().ContainKey("immutable_field");
        }

        [Fact]
        public async Task UpdateIdp_WellKnownUrlChanged_DiscoveryInvalid_ReturnsErrors()
        {
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(Idp());
            _repo.Setup(r => r.GetIdentityProviderByIdForViewAsync("idp-1")).ReturnsAsync(Idp());
            _saveIdpValidator.Setup(v => v.ValidateAsync(It.IsAny<SaveIdentityProviderRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("WellKnownUrl", "unreachable") }));

            var result = await Create().UpdateIdentityProviderAsync("idp-1",
                new UpdateIdentityProviderRequest { WellKnownUrl = "https://new.example/.well-known/openid-configuration" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("WellKnownUrl");
        }

        [Fact]
        public async Task UpdateIdp_WellKnownUrlChanged_DiscoveryValid_MergesAllFields()
        {
            var existing = Idp();
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.GetIdentityProviderByIdForViewAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(existing);

            var request = new UpdateIdentityProviderRequest
            {
                DisplayName = "Updated",
                IsActive = true,
                Issuer = "https://issuer",
                AuthorizationUrl = "https://auth",
                TokenUrl = "https://token",
                UserInfoUrl = "https://userinfo",
                JwksUri = "https://jwks",
                WellKnownUrl = "https://new.example/.well-known/openid-configuration",
                RedirectUris = new List<string> { "https://cb" },
                Scope = "openid email",
                ResponseType = "code",
                GrantTypes = new List<string> { "authorization_code" },
                RequirePkce = true,
                TokenEndpointAuthMethod = "client_secret_basic",
                InitialRoles = new List<string> { "role-1" },
                InitialPermissions = new List<string> { "perm-1" },
                Icon = "icon",
                TeamId = "team",
                KeyId = "key",
                PrivateKey = "pk",
                AppleAudience = "aud"
            };
            // #649: a supplied private key goes to Blocks Secrets (legacy provider: a new secret).
            _secrets.Setup(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Name == "identity-provider-private-key" && r.Value == "pk"), It.IsAny<CancellationToken>()))
                .ReturnsAsync("pk-ref");

            var result = await Create().UpdateIdentityProviderAsync("idp-1", request);

            result.IsSuccess.Should().BeTrue();
            existing.PrivateKey.Should().BeNull();
            existing.PrivateKeyReference.Should().Be("pk-ref");
            existing.ClientSecret.Should().Be("secret", "no client secret was supplied");
            existing.ClientSecretReference.Should().BeNull();
            existing.Issuer.Should().Be("https://issuer");
            existing.WellKnownUrl.Should().Be("https://new.example/.well-known/openid-configuration");
            existing.RequirePkce.Should().BeTrue();
            existing.AppleAudience.Should().Be("aud");
            existing.InitialRoles.Should().Contain("role-1");
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _secrets.VerifyNoOtherCalls();
        }

        // ---------- DeleteIdentityProviderAsync ----------

        [Fact]
        public async Task DeleteIdp_EmptyId_ReturnsError()
        {
            var result = await Create().DeleteIdentityProviderAsync("");
            result.Errors.Should().ContainKey("id_required");
        }

        [Fact]
        public async Task DeleteIdp_NotFound_ReturnsError()
        {
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync((IdentityProvider)null!);
            _repo.Setup(r => r.GetIdentityProviderByIdForViewAsync("idp-1")).ReturnsAsync((IdentityProvider)null!);
            var result = await Create().DeleteIdentityProviderAsync("idp-1");
            result.Errors.Should().ContainKey("not_found");
        }

        [Fact]
        public async Task DeleteIdp_HappyPath_DeletesAndCleansUpOidcClient()
        {
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(Idp());
            _repo.Setup(r => r.GetIdentityProviderByIdForViewAsync("idp-1")).ReturnsAsync(Idp());
            _repo.Setup(r => r.DeleteIdentityProviderAsync("idp-1")).Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid")).ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "cid" });
            _repo.Setup(r => r.DeleteOidcClientAsync(It.IsAny<DeleteOIDCClientRequest>())).Returns(Task.CompletedTask);

            var result = await Create().DeleteIdentityProviderAsync("idp-1");

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.DeleteIdentityProviderAsync("idp-1"), Times.Once);
            _repo.Verify(r => r.DeleteOidcClientAsync(It.Is<DeleteOIDCClientRequest>(x => x.ItemId == "cid")), Times.Once);
        }

        // ---------- UpdateIdentityProviderStatusAsync ----------

        [Fact]
        public async Task UpdateStatus_EmptyId_ReturnsError()
        {
            var result = await Create().UpdateIdentityProviderStatusAsync("", true);
            result.Errors.Should().ContainKey("id_required");
        }

        [Fact]
        public async Task UpdateStatus_NotFound_ReturnsError()
        {
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync((IdentityProvider)null!);
            _repo.Setup(r => r.GetIdentityProviderByIdForViewAsync("idp-1")).ReturnsAsync((IdentityProvider)null!);
            var result = await Create().UpdateIdentityProviderStatusAsync("idp-1", true);
            result.Errors.Should().ContainKey("not_found");
        }

        [Fact]
        public async Task UpdateStatus_HappyPath_SetsActive()
        {
            var existing = Idp();
            existing.IsActive = false;
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.GetIdentityProviderByIdForViewAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(existing);
            var result = await Create().UpdateIdentityProviderStatusAsync("idp-1", true);
            result.IsSuccess.Should().BeTrue();
            existing.IsActive.Should().BeTrue();
        }

        // ---------- Pass-through getters ----------

        [Fact]
        public async Task Getters_PassThroughToRepository()
        {
            _repo.Setup(r => r.GetIdentityProviderAsync("google")).ReturnsAsync(Idp());
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(Idp());
            _repo.Setup(r => r.GetIdentityProviderByIdForViewAsync("idp-1")).ReturnsAsync(Idp());
            _repo.Setup(r => r.GetIdentityProvidersAsync()).ReturnsAsync(new List<IdentityProvider> { Idp() });
            _repo.Setup(r => r.GetIdentityProvidersForViewAsync()).ReturnsAsync(new List<IdentityProvider> { Idp() });

            (await Create().GetIdentityProviderAsync("google")).Should().NotBeNull();
            (await Create().GetIdentityProviderByIdAsync("idp-1")).Should().NotBeNull();
            (await Create().GetAllIdentityProvidersAsync()).Should().HaveCount(1);
        }

        // ---------- RotateOidcClientSecretAsync ----------

        [Fact]
        public async Task Rotate_EmptyId_ReturnsError()
        {
            var result = await Create().RotateOidcClientSecretAsync("");
            result.Errors.Should().ContainKey("id_required");
        }

        [Fact]
        public async Task Rotate_NotFound_ReturnsError()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync((OidcClientRegistration)null!);
            var result = await Create().RotateOidcClientSecretAsync("c1");
            result.Errors.Should().ContainKey("not_found");
        }

        [Fact]
        public async Task Rotate_HappyPath_GeneratesNewSecret()
        {
            var cred = new OidcClientRegistration { ItemId = "c1", ClientId = "c1", ClientSecret = "old" };
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(cred);
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>())).Returns(Task.CompletedTask);
            OidcSecretStoreAccepts();
            var result = await Create().RotateOidcClientSecretAsync("c1");
            result.IsSuccess.Should().BeTrue();
            result.ClientSecret.Should().NotBe("old");
            result.ClientSecret.Should().StartWith("blxsk_");
            // A client still on plaintext gets a new secret in the store; the document keeps the reference only.
            cred.ClientSecret.Should().BeEmpty();
            cred.ClientSecretReference.Should().Be("oidc-ref-1");
            _secrets.Verify(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Value == result.ClientSecret && r.Type == SecretTypes.Service), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>The secret store takes a new OIDC client secret and answers <paramref name="reference"/>.</summary>
        private void OidcSecretStoreAccepts(string reference = "oidc-ref-1") =>
            _secrets.Setup(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Name == OidcClientSecretResolver.SecretName), It.IsAny<CancellationToken>()))
                .ReturnsAsync(reference);

        // ---------- SaveClientCredentialAsync ----------

        /// <summary>The secret store takes a new client-credential secret and answers <paramref name="reference"/>.</summary>
        private void ClientSecretStoreAccepts(string reference = "cs-ref-1") =>
            _secrets.Setup(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Name == ClientCredentialSecretResolver.SecretName), It.IsAny<CancellationToken>()))
                .ReturnsAsync(reference);

        [Fact]
        public async Task SaveClientCredential_EmptyName_ReturnsError()
        {
            var result = await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest { Name = "" });
            result.Errors.Should().ContainKey("invalid_request");
        }

        [Fact]
        public async Task SaveClientCredential_NoRolesOrPermissions_ReturnsError()
        {
            var result = await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest { Name = "svc", Roles = new(), Permissions = new() });
            result.Errors.Should().ContainKey("invalid_request");
        }

        [Fact]
        public async Task SaveClientCredential_UpdateNotFound_ReturnsError()
        {
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync((ClientCredential)null!);
            var result = await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest { ItemId = "cc-1", Name = "svc", Roles = new() { "r" } });
            result.Errors.Should().ContainKey("not_found");
        }

        [Fact]
        public async Task SaveClientCredential_Create_HappyPath()
        {
            ClientSecretStoreAccepts();
            _repo.Setup(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>())).ReturnsAsync(new BaseResponse { IsSuccess = true });
            var result = await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest { Name = "svc", Roles = new() { "r" }, Permissions = new() { "p" } });
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().NotBeNullOrEmpty();
            result.ClientSecret.Should().StartWith("blxk_");
            // The secret goes to the store; the document keeps the reference only.
            _secrets.Verify(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Value == result.ClientSecret && r.Type == SecretTypes.Service), It.IsAny<CancellationToken>()), Times.Once);
            _repo.Verify(r => r.SaveClientCredentialAsync(It.Is<ClientCredential>(c =>
                c.Name == "svc" && c.ClientSecret == string.Empty && c.ClientSecretReference == "cs-ref-1")), Times.Once);
        }

        [Fact]
        public async Task SaveClientCredential_Update_HappyPath()
        {
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync(new ClientCredential { ItemId = "cc-1", Name = "old" });
            _repo.Setup(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>())).ReturnsAsync(new BaseResponse { IsSuccess = true });
            var result = await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest { ItemId = "cc-1", Name = "new", Roles = new() { "r" } });
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be("cc-1");
            result.ClientSecret.Should().BeNull();
            _repo.Verify(r => r.SaveClientCredentialAsync(It.Is<ClientCredential>(c => c.Name == "new")), Times.Once);
        }

        [Fact]
        public async Task RotateClientCredentialSecret_ReplacesAndReturnsTheSecret()
        {
            ClientSecretStoreAccepts();
            var credential = new ClientCredential { ItemId = "cc-1", ClientSecret = "old", OrganizationId = "default" };
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync(credential);
            _repo.Setup(r => r.SaveClientCredentialAsync(credential)).ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await Create().RotateClientCredentialSecretAsync("cc-1");

            result.IsSuccess.Should().BeTrue();
            result.ClientSecret.Should().StartWith("blxk_").And.NotBe("old");
            // A credential still on plaintext gets a new secret in the store, which moves it there.
            credential.ClientSecret.Should().BeEmpty();
            credential.ClientSecretReference.Should().Be("cs-ref-1");
            _secrets.Verify(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Value == result.ClientSecret), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RotateClientCredentialSecret_OtherOrganization_ReportsNotFound()
        {
            SetOrganization("org-a");
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1"))
                .ReturnsAsync(new ClientCredential { ItemId = "cc-1", OrganizationId = "org-b" });

            var result = await Create().RotateClientCredentialSecretAsync("cc-1");

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("not_found");
            _repo.Verify(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>()), Times.Never);
        }

        // ---------- DeleteClientCredentialAsync ----------

        [Fact]
        public async Task DeleteClientCredential_EmptyId_ReturnsError()
        {
            var result = await Create().DeleteClientCredentialAsync(new DeleteClientCredentialRequest { ItemId = "" });
            result.Errors.Should().ContainKey("invalid_request");
        }

        [Fact]
        public async Task DeleteClientCredential_NotFound_ReturnsError()
        {
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync((ClientCredential)null!);
            var result = await Create().DeleteClientCredentialAsync(new DeleteClientCredentialRequest { ItemId = "cc-1" });
            result.Errors.Should().ContainKey("not_found");
        }

        [Fact]
        public async Task DeleteClientCredential_HappyPath()
        {
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync(new ClientCredential { ItemId = "cc-1", Name = "x" });
            _repo.Setup(r => r.DeleteClientCredentialAsync(It.IsAny<DeleteClientCredentialRequest>())).Returns(Task.CompletedTask);
            var result = await Create().DeleteClientCredentialAsync(new DeleteClientCredentialRequest { ItemId = "cc-1" });
            result.IsSuccess.Should().BeTrue();
        }

        // ---------- OIDC client getters + SaveOIDCClient + DeleteOidcClient ----------

        [Fact]
        public async Task GetOidcClient_ReturnsClient()
        {
            _repo.Setup(r => r.GetOIDCCredentialByIdAsync("c1")).ReturnsAsync(new OidcClientRegistration { ItemId = "c1", ClientId = "c1" });
            var result = await Create().GetOidcClientAsync("c1");
            result.IsSuccess.Should().BeTrue();
            result.oIDCClientCredential!.ItemId.Should().Be("c1");
        }

        [Fact]
        public async Task GetOidcClients_ReturnsList()
        {
            _repo.Setup(r => r.GetOIDCCredentialsByTenantAsync()).ReturnsAsync(new List<OidcClientRegistration> { new() { ItemId = "c1", ClientId = "c1" } });
            _repo.Setup(r => r.GetOIDCCredentialsByTenantForViewAsync()).ReturnsAsync(new List<OidcClientRegistration> { new() { ItemId = "c1", ClientId = "c1" } });
            var result = await Create().GetOidcClientsAsync();
            result.IsSuccess.Should().BeTrue();
            result.oIDCClientCredentials.Should().HaveCount(1);
        }

        [Fact]
        public async Task SaveOIDCClient_ValidationFails_ReturnsErrors()
        {
            _oidcValidator.Setup(v => v.ValidateAsync(It.IsAny<SaveOIDCClientRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("RedirectUris", "req") }));
            var result = await Create().SaveOIDCClientAsync(new SaveOIDCClientRequest());
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("RedirectUris");
        }

        [Fact]
        public async Task SaveOIDCClient_HappyPath_SavesAndDoesNotRegisterProvider()
        {
            OidcClientRegistration? saved = null;
            _repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>())).ReturnsAsync((OidcClientRegistration)null!);
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()))
                .Callback<OidcClientRegistration>(credential => saved = credential)
                .Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync((IdentityProvider)null!);
            OidcSecretStoreAccepts();

            var result = await Create().SaveOIDCClientAsync(new SaveOIDCClientRequest
            {
                RedirectUris = new() { "https://app/cb" },
                AllowedScopes = new() { "openid" },
                ClientType = "public",
                RegisterAsIdentityProvider = false
            });

            result.IsSuccess.Should().BeTrue();
            saved.Should().NotBeNull();
            saved!.AllowedScopes.Should().Contain("offline_access");
            _repo.Verify(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()), Times.Once);
            _repo.Verify(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
        }

        [Fact]
        public async Task SaveOIDCClient_DeviceFlowClient_ClearsAuthCodeMetadata()
        {
            OidcClientRegistration? saved = null;
            _repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>())).ReturnsAsync((OidcClientRegistration)null!);
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()))
                .Callback<OidcClientRegistration>(credential => saved = credential)
                .Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync((IdentityProvider)null!);

            var result = await Create().SaveOIDCClientAsync(new SaveOIDCClientRequest
            {
                IsDeviceFlowClient = true,
                RedirectUris = new() { "https://app/cb" },
                AllowedResponseTypes = new() { "code" },
                AllowedScopes = new() { "openid" },
                ClientType = "public",
                RegisterAsIdentityProvider = false
            });

            result.IsSuccess.Should().BeTrue();
            saved.Should().NotBeNull();
            saved!.IsDeviceFlowClient.Should().BeTrue();
            saved.RedirectUris.Should().BeEmpty();
            saved.AllowedResponseTypes.Should().BeEmpty();
            saved.AllowedScopes.Should().Contain("offline_access");
        }

        [Fact]
        public async Task SaveOIDCClient_DeviceFlowClient_WithoutClientType_DefaultsToPublic()
        {
            // The FE registration form never sends ClientType at all, so this is the real-world
            // payload shape for a device-flow client. It must not fall back to confidential,
            // otherwise the CLI's secret-less device-code token exchange is rejected.
            OidcClientRegistration? saved = null;
            _repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>())).ReturnsAsync((OidcClientRegistration)null!);
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()))
                .Callback<OidcClientRegistration>(credential => saved = credential)
                .Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync((IdentityProvider)null!);

            var result = await Create().SaveOIDCClientAsync(new SaveOIDCClientRequest
            {
                IsDeviceFlowClient = true,
                AllowedScopes = new() { "openid" },
                RegisterAsIdentityProvider = false
            });

            result.IsSuccess.Should().BeTrue();
            saved.Should().NotBeNull();
            saved!.ClientType.Should().Be("public");
            saved.TokenEndpointAuthMethod.Should().Be("none");
            saved.AllowedScopes.Should().Contain("offline_access");
        }

        [Fact]
        public async Task SaveOIDCClient_EditingDeviceFlowClient_CorrectsStaleConfidentialType()
        {
            // Simulates re-saving a client that was previously persisted as confidential
            // (the bug this test guards against) via the same edit path the admin UI uses.
            var existing = new OidcClientRegistration
            {
                ItemId = "item-1",
                ClientId = "client-1",
                ClientSecret = "existing-secret",
                IsDeviceFlowClient = true,
                ClientType = "confidential",
                TokenEndpointAuthMethod = "client_secret_post"
            };
            OidcClientRegistration? saved = null;
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("item-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()))
                .Callback<OidcClientRegistration>(credential => saved = credential)
                .Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync((IdentityProvider)null!);

            var result = await Create().SaveOIDCClientAsync(new SaveOIDCClientRequest
            {
                ItemId = "item-1",
                IsDeviceFlowClient = true,
                AllowedScopes = new() { "openid" },
                RegisterAsIdentityProvider = false
            });

            result.IsSuccess.Should().BeTrue();
            saved.Should().NotBeNull();
            saved!.ClientType.Should().Be("public");
            saved.TokenEndpointAuthMethod.Should().Be("none");
            saved.ClientSecret.Should().BeNull();
            saved.AllowedScopes.Should().Contain("offline_access");
        }

        [Fact]
        public async Task SaveOIDCClient_RegisterAsProvider_CreatesProvider()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>())).ReturnsAsync((OidcClientRegistration)null!);
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>())).Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync((IdentityProvider)null!);
            _repo.Setup(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(Idp());
            OidcSecretStoreAccepts();

            var result = await Create().SaveOIDCClientAsync(new SaveOIDCClientRequest
            {
                RedirectUris = new() { "https://app/cb" },
                AllowedScopes = new() { "openid" },
                ClientType = "confidential",
                ClientDisplayName = "My App",
                RegisterAsIdentityProvider = true
            });

            result.IsSuccess.Should().BeTrue();
            // Pin the classification, not just the call: "blocks-oidc" is the value the Identity
            // Provider gallery filters on, so a wrong type here hides the provider entirely.
            // The provider shares the client's secret reference and holds no plaintext.
            _repo.Verify(r => r.CreateIdentityProviderAsync(It.Is<IdentityProvider>(p =>
                p.Provider == "my-app" && p.ProviderType == "blocks-oidc"
                && p.ClientSecretReference == "oidc-ref-1" && p.ClientSecret == string.Empty)), Times.Once);
        }

        [Fact]
        public async Task DeleteOidcClient_HappyPath_CleansUpProvider()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(new OidcClientRegistration { ItemId = "c1", ClientId = "c1" });
            _repo.Setup(r => r.DeleteOidcClientAsync(It.IsAny<DeleteOIDCClientRequest>())).Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetIdentityProvidersAsync()).ReturnsAsync(new List<IdentityProvider> { Idp(clientId: "c1") });
            _repo.Setup(r => r.GetIdentityProvidersForViewAsync()).ReturnsAsync(new List<IdentityProvider> { Idp(clientId: "c1") });
            _repo.Setup(r => r.DeleteIdentityProviderAsync("idp-1")).Returns(Task.CompletedTask);

            var result = await Create().DeleteOidcClientAsync(new DeleteOIDCClientRequest { ItemId = "c1" });

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.DeleteIdentityProviderAsync("idp-1"), Times.Once);
        }

        // ---------- Utility methods ----------

        [Fact]
        public void GetDeviceInfo_EmptyUserAgent_ReturnsNull()
        {
            Create().GetDeviceInfo("").Should().BeNull();
        }

        [Fact]
        public void GetDeviceInfo_ValidUserAgent_ReturnsInfo()
        {
            var info = Create().GetDeviceInfo("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
            info.Should().NotBeNull();
        }

        [Fact]
        public async Task SendToQueueAsync_DispatchesConsumerMessage()
        {
            _message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<TestPayload>>())).Returns(Task.CompletedTask);
            await Create().SendToQueueAsync("queue-1", new TestPayload { Value = "x" });
            _message.Verify(m => m.SendToConsumerAsync(It.Is<ConsumerMessage<TestPayload>>(c => c.ConsumerName == "queue-1")), Times.Once);
        }

        [Fact]
        public async Task SendToTopicAsync_DispatchesMassConsumerMessage()
        {
            _message.Setup(m => m.SendToMassConsumerAsync(It.IsAny<ConsumerMessage<TestPayload>>())).Returns(Task.CompletedTask);
            await Create().SendToTopicAsync("topic-1", new TestPayload { Value = "x" });
            _message.Verify(m => m.SendToMassConsumerAsync(It.Is<ConsumerMessage<TestPayload>>(c => c.ConsumerName == "topic-1")), Times.Once);
        }

        public sealed class TestPayload { public string Value { get; set; } = ""; }

        // ---------- GetMetadataAsync ----------

        [Fact]
        public async Task GetMetadata_DeserializesDiscoveryDocument()
        {
            var httpClient = new HttpClient(new FakeHandler());
            _httpFactory.Setup(f => f.CreateClient("oidc-discovery")).Returns(httpClient);

            var result = await Create().GetMetadataAsync("https://test.com/.well-known/openid-configuration");

            result.Should().NotBeNull();
            result!.Issuer.Should().Be("https://test.com/");
        }

        // ---------- Secrets never leave through a read ----------

        [Fact]
        public async Task GetOidcClient_NeverReturnsSecret()
        {
            _repo.Setup(r => r.GetOIDCCredentialByIdAsync("c1")).ReturnsAsync(new OidcClientRegistration { ItemId = "c1", ClientId = "c1", ClientSecret = "s" });
            var result = await Create().GetOidcClientAsync("c1");
            result.oIDCClientCredential!.ClientSecret.Should().BeNull();
        }

        [Fact]
        public async Task GetOidcClients_NeverReturnSecrets()
        {
            _repo.Setup(r => r.GetOIDCCredentialsByTenantForViewAsync()).ReturnsAsync(new List<OidcClientRegistration> { new() { ItemId = "c1", ClientId = "c1", ClientSecret = "s" } });
            var result = await Create().GetOidcClientsAsync();
            result.oIDCClientCredentials.Should().OnlyContain(c => c.ClientSecret == null);
        }

        [Fact]
        public async Task GetClientCredentials_NeverReturnSecrets()
        {
            _repo.Setup(r => r.GetClientCredentialsAsync(It.IsAny<string?>())).ReturnsAsync(new List<ClientCredential> { new() { ItemId = "cc-1", ClientSecret = "s" } });
            var result = await Create().GetClientCredentialsAsync(new GetAllClientCredentialsRequest());
            result.Should().OnlyContain(c => c.ClientSecret == null);
        }

        [Fact]
        public async Task GetIdentityProviders_NeverReturnSecrets()
        {
            var listed = Idp();
            listed.PrivateKey = "pk";
            _repo.Setup(r => r.GetIdentityProvidersForViewAsync()).ReturnsAsync(new List<IdentityProvider> { listed });
            var single = Idp();
            single.PrivateKey = "pk";
            _repo.Setup(r => r.GetIdentityProviderByIdForViewAsync("idp-1")).ReturnsAsync(single);

            var all = await Create().GetAllIdentityProvidersAsync();
            var one = await Create().GetIdentityProviderByIdAsync("idp-1");

            all.Should().OnlyContain(p => p.ClientSecret == "" && p.PrivateKey == null);
            one!.ClientSecret.Should().BeEmpty();
            one.PrivateKey.Should().BeNull();
        }

        [Fact]
        public async Task UpdateIdp_NewSecret_ReplacesStoredSecret()
        {
            // #649 (H9): the new secret goes to Blocks Secrets and the plaintext field is blanked.
            var existing = Idp();
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(existing);
            _secrets.Setup(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Value == "new-secret"), It.IsAny<CancellationToken>())).ReturnsAsync("ref-new");
            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-secret" });
            result.IsSuccess.Should().BeTrue();
            existing.ClientSecret.Should().BeEmpty();
            existing.ClientSecretReference.Should().Be("ref-new");
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _secrets.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public async Task UpdateIdp_EmptySecret_KeepsStoredSecret(string? secret)
        {
            var existing = Idp();
            existing.PrivateKey = "pk";
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(existing);
            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = secret, PrivateKey = secret });
            result.IsSuccess.Should().BeTrue();
            existing.ClientSecret.Should().Be("secret");
            existing.PrivateKey.Should().Be("pk");
        }

        [Fact]
        public async Task UpdateIdp_MirrorOfLocalOidcClient_RejectsSecretChange()
        {
            var existing = Idp();
            existing.ProviderType = "blocks-oidc";
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid")).ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "cid", RegisterAsIdentityProvider = true });
            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-secret" });
            result.IsSuccess.Should().BeFalse();
            existing.ClientSecret.Should().Be("secret");
            _repo.Verify(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
        }

        [Fact]
        public async Task UpdateIdp_BlocksOidcProviderOfAnotherProject_ReplacesSecret()
        {
            var existing = Idp();
            existing.ProviderType = "blocks-oidc";
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid")).ReturnsAsync((OidcClientRegistration)null!);
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(existing);
            _secrets.Setup(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Value == "new-secret"), It.IsAny<CancellationToken>())).ReturnsAsync("ref-new");
            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-secret" });
            result.IsSuccess.Should().BeTrue();
            // #649: not a mirror, so the secret is stored in Blocks Secrets like any other provider's.
            existing.ClientSecret.Should().BeEmpty();
            existing.ClientSecretReference.Should().Be("ref-new");
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _secrets.VerifyNoOtherCalls();
        }

        // ---------- GetClientCredentialsAsync ----------

        [Fact]
        public async Task GetClientCredentials_ReturnsRepositoryResult()
        {
            var credentials = new List<ClientCredential> { new() { ItemId = "cc-1" } };
            _repo.Setup(r => r.GetClientCredentialsAsync(null)).ReturnsAsync(credentials);

            var result = await Create().GetClientCredentialsAsync(new GetAllClientCredentialsRequest());

            result.Should().HaveCount(1);
            result[0].ItemId.Should().Be("cc-1");
        }

        [Fact]
        public async Task GetClientCredentials_TenantWideCaller_ListsEveryOrganization()
        {
            _repo.Setup(r => r.GetClientCredentialsAsync(It.IsAny<string?>())).ReturnsAsync([]);

            await Create().GetClientCredentialsAsync(new GetAllClientCredentialsRequest());

            _repo.Verify(r => r.GetClientCredentialsAsync(null), Times.Once);
        }

        [Fact]
        public async Task GetClientCredentials_OrganizationCaller_ListsOnlyItsOwn()
        {
            // The listed documents carry ClientSecret, so an unscoped list would hand one
            // organization's administrator every other organization's secrets.
            SetOrganization("org-a");
            _repo.Setup(r => r.GetClientCredentialsAsync(It.IsAny<string?>())).ReturnsAsync([]);

            await Create().GetClientCredentialsAsync(new GetAllClientCredentialsRequest());

            _repo.Verify(r => r.GetClientCredentialsAsync("org-a"), Times.Once);
            _repo.Verify(r => r.GetClientCredentialsAsync(null), Times.Never);
        }

        [Fact]
        public async Task GetClientCredentials_CallerWithNoOrganization_ReturnsNothing()
        {
            SetOrganization("no-org");

            var result = await Create().GetClientCredentialsAsync(new GetAllClientCredentialsRequest());

            result.Should().BeEmpty();
            _repo.Verify(r => r.GetClientCredentialsAsync(It.IsAny<string?>()), Times.Never);
        }

        // ---------- Client credential organization scope ----------

        [Fact]
        public async Task SaveClientCredential_TenantWideCaller_IssuesForRequestedOrganization()
        {
            ClientSecretStoreAccepts();
            ClientCredential? saved = null;
            _repo.Setup(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>()))
                .Callback<ClientCredential>(c => saved = c)
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest
            {
                Name = "svc",
                Roles = new() { "r" },
                OrganizationId = "org-a"
            });

            result.IsSuccess.Should().BeTrue();
            saved!.OrganizationId.Should().Be("org-a");
        }

        [Fact]
        public async Task SaveClientCredential_TenantWideCaller_WithNoOrganization_StaysTenantWide()
        {
            ClientSecretStoreAccepts();
            ClientCredential? saved = null;
            _repo.Setup(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>()))
                .Callback<ClientCredential>(c => saved = c)
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest { Name = "svc", Roles = new() { "r" } });

            saved!.OrganizationId.Should().Be("default");
        }

        [Fact]
        public async Task SaveClientCredential_OrganizationCaller_CannotIssueForAnotherOrganization()
        {
            ClientSecretStoreAccepts();
            // The payload is discarded rather than rejected, so a caller that redundantly sends its
            // own id keeps working and one that sends someone else's gets its own instead.
            SetOrganization("org-a");
            ClientCredential? saved = null;
            _repo.Setup(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>()))
                .Callback<ClientCredential>(c => saved = c)
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest
            {
                Name = "svc",
                Roles = new() { "r" },
                OrganizationId = "org-b"
            });

            saved!.OrganizationId.Should().Be("org-a");
        }

        [Fact]
        public async Task SaveClientCredential_OrganizationCaller_CannotEscalateToTenantWide()
        {
            ClientSecretStoreAccepts();
            SetOrganization("org-a");
            ClientCredential? saved = null;
            _repo.Setup(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>()))
                .Callback<ClientCredential>(c => saved = c)
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest
            {
                Name = "svc",
                Roles = new() { "r" },
                OrganizationId = "default"
            });

            saved!.OrganizationId.Should().Be("org-a");
        }

        [Fact]
        public async Task SaveClientCredential_RejectsNoOrgAsATarget()
        {
            var result = await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest
            {
                Name = "svc",
                Roles = new() { "r" },
                OrganizationId = "no-org"
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("invalid_request");
            _repo.Verify(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>()), Times.Never);
        }

        [Fact]
        public async Task SaveClientCredential_CallerWithNoOrganization_IsDenied()
        {
            SetOrganization("no-org");

            var result = await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest { Name = "svc", Roles = new() { "r" } });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("forbidden");
            _repo.Verify(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>()), Times.Never);
        }

        [Fact]
        public async Task SaveClientCredential_HonoursRequestedInactiveStatus()
        {
            ClientSecretStoreAccepts();
            ClientCredential? saved = null;
            _repo.Setup(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>()))
                .Callback<ClientCredential>(c => saved = c)
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest
            {
                Name = "svc",
                Roles = new() { "r" },
                IsActive = false
            });

            saved!.IsActive.Should().BeFalse();
        }

        [Fact]
        public async Task SaveClientCredential_Update_OtherOrganization_ReportsNotFound()
        {
            SetOrganization("org-a");
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1"))
                .ReturnsAsync(new ClientCredential { ItemId = "cc-1", OrganizationId = "org-b" });

            var result = await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest
            {
                ItemId = "cc-1",
                Name = "new",
                Roles = new() { "r" }
            });

            result.Errors.Should().ContainKey("not_found");
            _repo.Verify(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>()), Times.Never);
        }

        [Fact]
        public async Task SaveClientCredential_Update_DoesNotChangeOrganization()
        {
            ClientCredential? saved = null;
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1"))
                .ReturnsAsync(new ClientCredential { ItemId = "cc-1", OrganizationId = "org-a" });
            _repo.Setup(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>()))
                .Callback<ClientCredential>(c => saved = c)
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest
            {
                ItemId = "cc-1",
                Name = "new",
                Roles = new() { "r" },
                OrganizationId = "org-b"
            });

            saved!.OrganizationId.Should().Be("org-a");
        }

        [Fact]
        public async Task DeleteClientCredential_OtherOrganization_ReportsNotFound()
        {
            SetOrganization("org-a");
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1"))
                .ReturnsAsync(new ClientCredential { ItemId = "cc-1", OrganizationId = "org-b" });

            var result = await Create().DeleteClientCredentialAsync(new DeleteClientCredentialRequest { ItemId = "cc-1" });

            result.Errors.Should().ContainKey("not_found");
            _repo.Verify(r => r.DeleteClientCredentialAsync(It.IsAny<DeleteClientCredentialRequest>()), Times.Never);
        }

        // ---------- #649: identity-provider secrets in Blocks Secrets ----------

        private const string StoreUnavailable = "The secret store is unavailable. Try again later.";
        private const string ValueRejected = "The value cannot be stored in the secret store.";

        private AuthenticationDomainService CreateWithResolverLogger(ILogger<IdentityProviderSecretResolver> logger) =>
            new(_message.Object, _repo.Object,
                new AuthenticationRequestValidators(_oidcValidator.Object, _oidcUiTemplateValidator.Object, _saveIdpValidator.Object,
                    _updateIdpValidator.Object), _httpFactory.Object, TestDoubles.IdentityProviderSecretTestDoubles.Resolver(_secrets, logger), TestDoubles.ClientCredentialSecretTestDoubles.Resolver(_secrets), TestDoubles.OidcClientSecretTestDoubles.Resolver(_secrets));

        private AuthenticationDomainService CreateWithLogger(ILogger<AuthenticationDomainService> logger) =>
            new(_message.Object, _repo.Object,
                new AuthenticationRequestValidators(_oidcValidator.Object, _oidcUiTemplateValidator.Object, _saveIdpValidator.Object,
                    _updateIdpValidator.Object), _httpFactory.Object, TestDoubles.IdentityProviderSecretTestDoubles.Resolver(_secrets), TestDoubles.ClientCredentialSecretTestDoubles.Resolver(_secrets), TestDoubles.OidcClientSecretTestDoubles.Resolver(_secrets), logger);

        private const string RotationNotUndone = "cannot be undone";

        private void NoDuplicates()
        {
            _repo.Setup(r => r.GetIdentityProviderAsync("google")).ReturnsAsync((IdentityProvider)null!);
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync("cid")).ReturnsAsync((IdentityProvider)null!);
        }

        private static IdentityProvider Referenced(string? clientSecretRef = "cs-ref", string? privateKeyRef = null)
        {
            var p = Idp();
            p.ClientSecret = string.Empty;
            p.ClientSecretReference = clientSecretRef;
            p.PrivateKeyReference = privateKeyRef;
            return p;
        }

        [Fact]
        public async Task CreateIdp_WithClientSecret_StoresServiceSecretAndPersistsReferenceOnly()
        {
            // H2, E1, C13 (one SetAsync)
            NoDuplicates();
            IdentityProvider? inserted = null;
            _repo.Setup(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>())).Callback<IdentityProvider>(p => inserted = p).ReturnsAsync(Idp());
            SetSecretRequest? stored = null;
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()))
                .Callback<SetSecretRequest, CancellationToken>((r, _) => stored = r).ReturnsAsync("cs-ref");
            var req = SaveReq();
            req.ClientSecret = "g-secret";

            var result = await Create().CreateIdentityProviderAsync(req);

            result.IsSuccess.Should().BeTrue();
            stored!.Name.Should().Be("identity-provider-client-secret");
            stored.Type.Should().Be(SecretTypes.Service);
            stored.Value.Should().Be("g-secret");
            stored.Description.Should().Be("Identity provider google (tenant tenant-1): client secret");
            inserted!.ClientSecret.Should().BeEmpty();
            inserted.ClientSecretReference.Should().Be("cs-ref");
            inserted.PrivateKeyReference.Should().BeNull();
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CreateIdp_WithPrivateKey_StoresPrivateKeySecret()
        {
            // H3
            NoDuplicates();
            IdentityProvider? inserted = null;
            _repo.Setup(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>())).Callback<IdentityProvider>(p => inserted = p).ReturnsAsync(Idp());
            _secrets.Setup(s => s.SetAsync(It.Is<SetSecretRequest>(r =>
                    r.Name == "identity-provider-private-key" && r.Type == "service" && r.Value == "PEM"
                    && r.Description == "Identity provider google (tenant tenant-1): private key"), It.IsAny<CancellationToken>()))
                .ReturnsAsync("pk-ref");
            var req = SaveReq();
            req.PrivateKey = "PEM";

            var result = await Create().CreateIdentityProviderAsync(req);

            result.IsSuccess.Should().BeTrue();
            inserted!.PrivateKey.Should().BeNull();
            inserted.PrivateKeyReference.Should().Be("pk-ref");
            inserted.ClientSecretReference.Should().BeNull();
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CreateIdp_WithBothValues_UsesOneAllOrNothingBatch()
        {
            // H2, H3, C6, C13 (at most one write call)
            NoDuplicates();
            IdentityProvider? inserted = null;
            _repo.Setup(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>())).Callback<IdentityProvider>(p => inserted = p).ReturnsAsync(Idp());
            IReadOnlyCollection<SetSecretRequest>? batch = null;
            _secrets.Setup(s => s.SetManyAsync(It.IsAny<IReadOnlyCollection<SetSecretRequest>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyCollection<SetSecretRequest>, CancellationToken>((r, _) => batch = r)
                .ReturnsAsync(new Dictionary<string, string> { ["identity-provider-client-secret"] = "cs-ref", ["identity-provider-private-key"] = "pk-ref" });
            var req = SaveReq();
            req.ClientSecret = "s";
            req.PrivateKey = "PEM";

            var result = await Create().CreateIdentityProviderAsync(req);

            result.IsSuccess.Should().BeTrue();
            batch!.Select(b => b.Name).Should().BeEquivalentTo("identity-provider-client-secret", "identity-provider-private-key");
            batch.Should().OnlyContain(b => b.Type == "service");
            inserted!.ClientSecretReference.Should().Be("cs-ref");
            inserted.PrivateKeyReference.Should().Be("pk-ref");
            inserted.ClientSecret.Should().BeEmpty();
            inserted.PrivateKey.Should().BeNull();
            _secrets.Verify(s => s.SetManyAsync(It.IsAny<IReadOnlyCollection<SetSecretRequest>>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CreateIdp_BothValues_BatchFails_StoresNeitherAndCreatesNoDocument()
        {
            // C6, C1
            NoDuplicates();
            _secrets.Setup(s => s.SetManyAsync(It.IsAny<IReadOnlyCollection<SetSecretRequest>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecretVaultException("down", "set", "x"));
            var req = SaveReq();
            req.ClientSecret = "s";
            req.PrivateKey = "PEM";

            var result = await Create().CreateIdentityProviderAsync(req);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Equal(new Dictionary<string, string> { ["secret_store_unavailable"] = StoreUnavailable });
            _repo.Verify(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
            _secrets.Verify(s => s.SetManyAsync(It.IsAny<IReadOnlyCollection<SetSecretRequest>>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CreateIdp_VaultUnavailable_ReturnsSecretStoreUnavailable_AndInsertsNothing()
        {
            // C1, E6
            NoDuplicates();
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new SecretVaultException("down", "set", "x"));
            var req = SaveReq();
            req.ClientSecret = "g-secret";

            var result = await Create().CreateIdentityProviderAsync(req);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Equal(new Dictionary<string, string> { ["secret_store_unavailable"] = StoreUnavailable });
            _repo.Verify(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData("ClientSecret")]
        [InlineData("PrivateKey")]
        public async Task CreateIdp_ValueRejected_ReturnsPerFieldError_AndInsertsNothing(string field)
        {
            // C1 (SecretValidationException -> per-field error)
            NoDuplicates();
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new SecretValidationException("too large"));
            var req = SaveReq();
            if (field == "ClientSecret") req.ClientSecret = "x"; else req.PrivateKey = "x";

            var result = await Create().CreateIdentityProviderAsync(req);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Equal(new Dictionary<string, string> { [field] = ValueRejected });
            _repo.Verify(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CreateIdp_BothValuesRejected_ReportsBothFields_AndInsertsNothing()
        {
            // C1: the batch is all-or-nothing and does not say which value was refused.
            NoDuplicates();
            _secrets.Setup(s => s.SetManyAsync(It.IsAny<IReadOnlyCollection<SetSecretRequest>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecretValidationException("too large"));
            var req = SaveReq();
            req.ClientSecret = "s";
            req.PrivateKey = "PEM";

            var result = await Create().CreateIdentityProviderAsync(req);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Equal(new Dictionary<string, string> { ["ClientSecret"] = ValueRejected, ["PrivateKey"] = ValueRejected });
            _repo.Verify(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
            _secrets.Verify(s => s.SetManyAsync(It.IsAny<IReadOnlyCollection<SetSecretRequest>>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CreateIdp_OtherSecretFailure_Propagates_AndInsertsNothing()
        {
            NoDuplicates();
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new SecretAccessDeniedException("denied"));
            var req = SaveReq();
            req.ClientSecret = "x";

            var act = () => Create().CreateIdentityProviderAsync(req);

            await act.Should().ThrowAsync<SecretAccessDeniedException>();
            _repo.Verify(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CreateIdp_InsertFails_RetiresTheSecretsCreated_AndRethrows()
        {
            // C5
            NoDuplicates();
            _repo.Setup(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ThrowsAsync(new InvalidOperationException("mongo down"));
            _secrets.Setup(s => s.SetManyAsync(It.IsAny<IReadOnlyCollection<SetSecretRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, string> { ["identity-provider-client-secret"] = "cs-ref", ["identity-provider-private-key"] = "pk-ref" });
            _secrets.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var req = SaveReq();
            req.ClientSecret = "s";
            req.PrivateKey = "PEM";

            var act = () => Create().CreateIdentityProviderAsync(req);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("mongo down");
            _secrets.Verify(s => s.SetManyAsync(It.IsAny<IReadOnlyCollection<SetSecretRequest>>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.DeleteAsync("cs-ref", It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.DeleteAsync("pk-ref", It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CreateIdp_InsertThrowsButCommitted_KeepsTheSecretsTheDocumentPointsAt()
        {
            // M2: a timeout after the write; the stored document references the new secrets.
            NoDuplicates();
            _repo.Setup(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ThrowsAsync(new TimeoutException("timeout"));
            _repo.Setup(r => r.GetIdentityProviderByIdAsync(It.IsAny<string>())).ReturnsAsync(Referenced("cs-ref"));
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync("cs-ref");
            var req = SaveReq();
            req.ClientSecret = "s";

            var act = () => Create().CreateIdentityProviderAsync(req);

            await act.Should().ThrowAsync<TimeoutException>();
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CreateIdp_InsertThrows_AndTheReReadFails_KeepsTheSecrets_AndLogsTheTypeOnly()
        {
            // M2: fail safe, an orphan over a dangling reference.
            NoDuplicates();
            _repo.Setup(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ThrowsAsync(new TimeoutException("timeout"));
            _repo.Setup(r => r.GetIdentityProviderByIdAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("read failed cs-ref"));
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync("cs-ref");
            var logger = new TestDoubles.CapturingLogger<AuthenticationDomainService>();
            var req = SaveReq();
            req.ClientSecret = "g-secret";

            var act = () => CreateWithLogger(logger).CreateIdentityProviderAsync(req);

            await act.Should().ThrowAsync<TimeoutException>();
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _secrets.VerifyNoOtherCalls();
            var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
            warning.AllText.Should().Contain("google").And.Contain(nameof(InvalidOperationException))
                .And.NotContain("cs-ref").And.NotContain("g-secret").And.NotContain("read failed");
        }

        [Fact]
        public async Task CreateIdp_NoSecretSupplied_MakesNoSecretCall()
        {
            // C13, H7 (an empty value leaves the field as today)
            NoDuplicates();
            IdentityProvider? inserted = null;
            _repo.Setup(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>())).Callback<IdentityProvider>(p => inserted = p).ReturnsAsync(Idp());
            var req = SaveReq();
            req.ClientSecret = "";

            (await Create().CreateIdentityProviderAsync(req)).IsSuccess.Should().BeTrue();

            inserted!.ClientSecretReference.Should().BeNull();
            inserted.PrivateKeyReference.Should().BeNull();
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UpdateIdp_ReferencedProvider_RotatesInPlace_KeepingTheReference()
        {
            // H8, C13 (one RotateAsync)
            var existing = Referenced();
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(existing);
            _secrets.Setup(s => s.RotateAsync("cs-ref", It.Is<RotateSecretRequest>(r => r.Value == "new-s"), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-s" });

            result.IsSuccess.Should().BeTrue();
            existing.ClientSecretReference.Should().Be("cs-ref");
            existing.ClientSecret.Should().BeEmpty();
            _secrets.Verify(s => s.RotateAsync("cs-ref", It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
            _repo.Verify(r => r.UpdateIdentityProviderAsync(existing), Times.Once);
        }

        [Fact]
        public async Task UpdateIdp_LegacyProvider_StoresNewSecret_SetsReference_BlanksPlaintext()
        {
            // H9, E4
            var existing = Idp();
            existing.ClientSecret = "old-s";
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(existing);
            _secrets.Setup(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Name == "identity-provider-client-secret" && r.Type == "service" && r.Value == "new-s"), It.IsAny<CancellationToken>()))
                .ReturnsAsync("new-ref");

            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-s" });

            result.IsSuccess.Should().BeTrue();
            existing.ClientSecret.Should().BeEmpty();
            existing.ClientSecretReference.Should().Be("new-ref");
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        public static TheoryData<Exception> UnusableReference => new()
        {
            new SecretNotFoundException("cs-ref"),
            new SecretStateException("deleted", "rotate"),
        };

        [Theory]
        [MemberData(nameof(UnusableReference))]
        public async Task UpdateIdp_RotationOfUnusableReference_StoresNewSecretAndRepoints(Exception rotateFailure)
        {
            // C7: the old secret is left as it is (no delete).
            var existing = Referenced();
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(existing);
            _secrets.Setup(s => s.RotateAsync("cs-ref", It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(rotateFailure);
            _secrets.Setup(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Value == "new-s"), It.IsAny<CancellationToken>())).ReturnsAsync("new-ref");

            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-s" });

            result.IsSuccess.Should().BeTrue();
            existing.ClientSecretReference.Should().Be("new-ref");
            existing.ClientSecret.Should().BeEmpty();
            // Budget: one failed rotate plus one set.
            _secrets.Verify(s => s.RotateAsync("cs-ref", It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UpdateIdp_BothSecretsReferenced_RotatesBothInPlace()
        {
            // H8 for both credentials: two rotations, references unchanged, plaintext blanked.
            var existing = Referenced(privateKeyRef: "pk-ref");
            existing.PrivateKey = "stale-pem";
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(existing);
            _secrets.Setup(s => s.RotateAsync("cs-ref", It.Is<RotateSecretRequest>(r => r.Value == "new-s"), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _secrets.Setup(s => s.RotateAsync("pk-ref", It.Is<RotateSecretRequest>(r => r.Value == "new-pem"), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-s", PrivateKey = "new-pem" });

            result.IsSuccess.Should().BeTrue();
            existing.ClientSecretReference.Should().Be("cs-ref");
            existing.PrivateKeyReference.Should().Be("pk-ref");
            existing.ClientSecret.Should().BeEmpty();
            existing.PrivateKey.Should().BeNull();
            _secrets.Verify(s => s.RotateAsync("cs-ref", It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.RotateAsync("pk-ref", It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
            _repo.Verify(r => r.UpdateIdentityProviderAsync(existing), Times.Once);
        }

        [Fact]
        public async Task UpdateIdp_ReferencedProvider_NoSecretSupplied_ChangesOtherFieldsWithNoSecretCall()
        {
            // H10, E5, C13
            var existing = Referenced(privateKeyRef: "pk-ref");
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(existing);

            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "", PrivateKey = null, DisplayName = "X" });

            result.IsSuccess.Should().BeTrue();
            existing.DisplayName.Should().Be("X");
            existing.ClientSecretReference.Should().Be("cs-ref");
            existing.PrivateKeyReference.Should().Be("pk-ref");
            _secrets.VerifyNoOtherCalls();
            _repo.Verify(r => r.GetIdentityProviderByIdAsync("idp-1"), Times.Once);
        }

        [Fact]
        public async Task UpdateIdp_VaultUnavailable_ReturnsSecretStoreUnavailable_AndWritesNoDocument()
        {
            // C1
            var existing = Referenced();
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _secrets.Setup(s => s.RotateAsync("cs-ref", It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new SecretVaultException("down", "rotate", "cs-ref"));

            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-s", DisplayName = "X" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Equal(new Dictionary<string, string> { ["secret_store_unavailable"] = StoreUnavailable });
            _repo.Verify(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
            _secrets.Verify(s => s.RotateAsync("cs-ref", It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UpdateIdp_StoreFailsAfterARotation_ReportsUnavailable_AndLogsThatTheRotationCannotBeUndone()
        {
            // The client secret rotated, then storing the private key failed: no document write.
            var existing = Referenced();
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _secrets.Setup(s => s.RotateAsync("cs-ref", It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new SecretVaultException("down", "set", "x"));
            var logger = new TestDoubles.CapturingLogger<AuthenticationDomainService>();

            var result = await CreateWithLogger(logger).UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-s", PrivateKey = "PEM" });

            result.Errors.Should().Equal(new Dictionary<string, string> { ["secret_store_unavailable"] = StoreUnavailable });
            _repo.Verify(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
            _secrets.Verify(s => s.RotateAsync("cs-ref", It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
            logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning)
                .Which.AllText.Should().Contain(RotationNotUndone).And.NotContain("cs-ref").And.NotContain("new-s");
        }

        [Fact]
        public async Task UpdateIdp_OtherSecretFailure_Propagates_AndRetiresTheSecretCreatedFirst()
        {
            // Any SecretException other than vault or validation goes to the global handler; no new orphan.
            var existing = Idp();
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _secrets.Setup(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Name == "identity-provider-client-secret"), It.IsAny<CancellationToken>())).ReturnsAsync("cs-new");
            _secrets.Setup(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Name == "identity-provider-private-key"), It.IsAny<CancellationToken>())).ThrowsAsync(new SecretAccessDeniedException("denied"));
            _secrets.Setup(s => s.DeleteAsync("cs-new", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var act = () => Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "s", PrivateKey = "PEM" });

            await act.Should().ThrowAsync<SecretAccessDeniedException>();
            _repo.Verify(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            _secrets.Verify(s => s.DeleteAsync("cs-new", It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UpdateIdp_PrivateKeyRejected_ReturnsPerFieldError_AndRetiresTheClientSecretCreatedFirst()
        {
            // C1 (per-field error), C5 (no new orphan)
            var existing = Idp();
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _secrets.Setup(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Name == "identity-provider-client-secret"), It.IsAny<CancellationToken>())).ReturnsAsync("cs-new");
            _secrets.Setup(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Name == "identity-provider-private-key"), It.IsAny<CancellationToken>())).ThrowsAsync(new SecretValidationException("too large"));
            _secrets.Setup(s => s.DeleteAsync("cs-new", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "s", PrivateKey = "PEM" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Equal(new Dictionary<string, string> { ["PrivateKey"] = ValueRejected });
            _secrets.Verify(s => s.DeleteAsync("cs-new", It.IsAny<CancellationToken>()), Times.Once);
            _repo.Verify(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UpdateIdp_DocumentWriteFails_RetiresTheNewSecret_AndRethrows()
        {
            // C5
            // The re-read finds the stored document still legacy: the write did not land.
            var existing = Idp();
            _repo.SetupSequence(r => r.GetIdentityProviderByIdAsync("idp-1"))
                .ReturnsAsync(existing)
                .ReturnsAsync(Idp());
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ThrowsAsync(new InvalidOperationException("mongo down"));
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync("new-ref");
            _secrets.Setup(s => s.DeleteAsync("new-ref", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var act = () => Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-s" });

            await act.Should().ThrowAsync<InvalidOperationException>();
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.DeleteAsync("new-ref", It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UpdateIdp_DocumentWriteThrowsButCommitted_KeepsTheNewSecret()
        {
            // M2: a timeout after the replace; the stored document points at the new secret.
            var existing = Idp();
            var committed = Idp();
            committed.ClientSecret = string.Empty;
            committed.ClientSecretReference = "new-ref";
            _repo.SetupSequence(r => r.GetIdentityProviderByIdAsync("idp-1"))
                .ReturnsAsync(existing)
                .ReturnsAsync(committed);
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ThrowsAsync(new TimeoutException("timeout"));
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync("new-ref");

            var act = () => Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-s" });

            await act.Should().ThrowAsync<TimeoutException>();
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UpdateIdp_DocumentWriteThrows_AndTheReReadFails_KeepsTheNewSecret_AndLogsTheTypeOnly()
        {
            // M2: fail safe, an orphan over a dangling reference.
            var existing = Idp();
            _repo.SetupSequence(r => r.GetIdentityProviderByIdAsync("idp-1"))
                .ReturnsAsync(existing)
                .ThrowsAsync(new InvalidOperationException("read failed new-ref"));
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ThrowsAsync(new TimeoutException("timeout"));
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync("new-ref");
            var logger = new TestDoubles.CapturingLogger<AuthenticationDomainService>();

            var act = () => CreateWithLogger(logger).UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-s" });

            await act.Should().ThrowAsync<TimeoutException>();
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _secrets.VerifyNoOtherCalls();
            var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
            warning.AllText.Should().Contain("google").And.Contain(nameof(InvalidOperationException))
                .And.NotContain("new-ref").And.NotContain("new-s").And.NotContain("read failed");
        }

        [Fact]
        public async Task UpdateIdp_DocumentWriteFails_AfterRotation_LeavesTheSecretInPlace()
        {
            // C5: a rotation cannot be undone; the reference secret is never deleted.
            var existing = Referenced();
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ThrowsAsync(new InvalidOperationException("mongo down"));
            _secrets.Setup(s => s.RotateAsync("cs-ref", It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var act = () => Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-s" });

            await act.Should().ThrowAsync<InvalidOperationException>();
            _secrets.Verify(s => s.RotateAsync("cs-ref", It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UpdateIdp_DocumentWriteFails_AfterRotation_LogsThatTheRotationCannotBeUndone()
        {
            // M4 / spec section 6: a rotation cannot be undone, so it is logged (no value or reference id).
            var existing = Referenced();
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ThrowsAsync(new InvalidOperationException("mongo down"));
            _secrets.Setup(s => s.RotateAsync("cs-ref", It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var logger = new TestDoubles.CapturingLogger<AuthenticationDomainService>();

            var act = () => CreateWithLogger(logger).UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-s" });

            await act.Should().ThrowAsync<InvalidOperationException>();
            var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
            warning.AllText.Should().Contain("google").And.Contain(RotationNotUndone).And.NotContain("cs-ref").And.NotContain("new-s");
        }

        [Fact]
        public async Task UpdateIdp_DeniedWrite_RetiresTheNewSecret_AndReportsAsToday()
        {
            // A data-access denial makes the replace a silent no-op; the stored provider is still legacy.
            var existing = Idp();
            _repo.SetupSequence(r => r.GetIdentityProviderByIdAsync("idp-1"))
                .ReturnsAsync(existing)
                .ReturnsAsync(Idp());
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(existing);
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync("new-ref");
            _secrets.Setup(s => s.DeleteAsync("new-ref", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-s" });

            result.IsSuccess.Should().BeTrue();
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.DeleteAsync("new-ref", It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UpdateIdp_DeniedWrite_WithARotation_LogsThatTheRotationCannotBeUndone()
        {
            // M4: the client secret rotates and a new private key is stored; the write is denied.
            var existing = Referenced();
            _repo.SetupSequence(r => r.GetIdentityProviderByIdAsync("idp-1"))
                .ReturnsAsync(existing)
                .ReturnsAsync(Referenced());
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(existing);
            _secrets.Setup(s => s.RotateAsync("cs-ref", It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _secrets.Setup(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Name == "identity-provider-private-key"), It.IsAny<CancellationToken>())).ReturnsAsync("pk-new");
            _secrets.Setup(s => s.DeleteAsync("pk-new", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var logger = new TestDoubles.CapturingLogger<AuthenticationDomainService>();

            var result = await CreateWithLogger(logger).UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-s", PrivateKey = "PEM" });

            result.IsSuccess.Should().BeTrue();
            _secrets.Verify(s => s.RotateAsync("cs-ref", It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.DeleteAsync("pk-new", It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
            var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
            warning.AllText.Should().Contain("google").And.Contain(RotationNotUndone)
                .And.NotContain("cs-ref").And.NotContain("pk-new").And.NotContain("new-s").And.NotContain("PEM");
        }

        [Fact]
        public async Task UpdateIdp_ReReadFindsNoDocument_KeepsTheNewSecret()
        {
            // M1: a read rule on a field the update changed can hide the document; never retire then.
            var existing = Idp();
            _repo.SetupSequence(r => r.GetIdentityProviderByIdAsync("idp-1"))
                .ReturnsAsync(existing)
                .ReturnsAsync((IdentityProvider)null!);
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(existing);
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync("new-ref");
            var logger = new TestDoubles.CapturingLogger<AuthenticationDomainService>();

            var result = await CreateWithLogger(logger).UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-s", IsActive = false });

            result.IsSuccess.Should().BeTrue();
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _secrets.VerifyNoOtherCalls();
            logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Which.AllText.Should().Contain("google").And.NotContain("new-ref");
        }

        [Fact]
        public async Task UpdateIdp_ReReadFails_StillSucceeds_AndKeepsTheNewSecret()
        {
            // M5: the update is committed; a failing check must not turn it into an error.
            var existing = Idp();
            _repo.SetupSequence(r => r.GetIdentityProviderByIdAsync("idp-1"))
                .ReturnsAsync(existing)
                .ThrowsAsync(new InvalidOperationException("read failed"));
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(existing);
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync("new-ref");
            var logger = new TestDoubles.CapturingLogger<AuthenticationDomainService>();

            var result = await CreateWithLogger(logger).UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-s" });

            result.IsSuccess.Should().BeTrue();
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _secrets.VerifyNoOtherCalls();
            logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning)
                .Which.AllText.Should().Contain(nameof(InvalidOperationException)).And.NotContain("read failed").And.NotContain("new-ref");
        }

        [Fact]
        public async Task UpdateIdp_MirrorOfLocalOidcClient_RejectsSecret_WithNoSecretCall()
        {
            // C11
            var existing = Idp();
            existing.ProviderType = "blocks-oidc";
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid")).ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "cid", RegisterAsIdentityProvider = true });

            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientSecret = "new-secret" });

            result.Errors.Should().ContainKey("immutable_field");
            _secrets.VerifyNoOtherCalls();
        }

        private void DeletableProvider(IdentityProvider existing, List<string> order)
        {
            _repo.SetupSequence(r => r.GetIdentityProviderByIdAsync("idp-1"))
                .ReturnsAsync(existing)
                .ReturnsAsync((IdentityProvider)null!);
            _repo.Setup(r => r.DeleteIdentityProviderAsync("idp-1")).Callback(() => order.Add("document")).Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid")).ReturnsAsync((OidcClientRegistration)null!);
        }

        [Fact]
        public async Task DeleteIdp_WithReferences_RetiresEachSecretAfterTheDocumentDelete()
        {
            // H11, C13 (one DeleteAsync per reference)
            var order = new List<string>();
            DeletableProvider(Referenced(privateKeyRef: "pk-ref"), order);
            _secrets.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((id, _) => order.Add(id)).Returns(Task.CompletedTask);

            var result = await Create().DeleteIdentityProviderAsync("idp-1");

            result.IsSuccess.Should().BeTrue();
            order.Should().Equal("document", "cs-ref", "pk-ref");
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task DeleteIdp_LegacyProvider_MakesNoSecretCall()
        {
            var order = new List<string>();
            DeletableProvider(Idp(), order);

            (await Create().DeleteIdentityProviderAsync("idp-1")).IsSuccess.Should().BeTrue();

            _secrets.VerifyNoOtherCalls();
        }

        [Theory]
        [MemberData(nameof(UnusableReference))]
        public async Task DeleteIdp_SecretAlreadyGone_IsIgnored(Exception failure)
        {
            // C12
            DeletableProvider(Referenced(), new List<string>());
            _secrets.Setup(s => s.DeleteAsync("cs-ref", It.IsAny<CancellationToken>())).ThrowsAsync(failure);
            var logger = new TestDoubles.CapturingLogger<IdentityProviderSecretResolver>();

            var result = await CreateWithResolverLogger(logger).DeleteIdentityProviderAsync("idp-1");

            result.IsSuccess.Should().BeTrue();
            logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
            _secrets.Verify(s => s.DeleteAsync("cs-ref", It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task DeleteIdp_RetireFailsOtherwise_LogsAWarning_AndStillSucceeds()
        {
            // C12, C8 (no reference id in the log)
            DeletableProvider(Referenced(), new List<string>());
            _secrets.Setup(s => s.DeleteAsync("cs-ref", It.IsAny<CancellationToken>())).ThrowsAsync(new SecretVaultException("down cs-ref", "delete", "cs-ref"));
            var logger = new TestDoubles.CapturingLogger<IdentityProviderSecretResolver>();

            var result = await CreateWithResolverLogger(logger).DeleteIdentityProviderAsync("idp-1");

            result.IsSuccess.Should().BeTrue();
            var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
            warning.AllText.Should().Contain("google").And.Contain(nameof(SecretVaultException)).And.NotContain("cs-ref");
            _secrets.Verify(s => s.DeleteAsync("cs-ref", It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task DeleteIdp_DeniedDelete_KeepsTheSecretsOfTheStillExistingProvider()
        {
            // A denied delete is a silent no-op: the provider still exists, so its secrets stay.
            var existing = Referenced();
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
            _repo.Setup(r => r.DeleteIdentityProviderAsync("idp-1")).Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid")).ReturnsAsync((OidcClientRegistration)null!);

            (await Create().DeleteIdentityProviderAsync("idp-1")).IsSuccess.Should().BeTrue();

            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task DeleteIdp_ReReadFails_StillSucceeds_KeepsTheSecrets_AndDeletesTheRelatedOidcClient()
        {
            // M5: the delete completed; a failing check neither fails it nor skips the OIDC cleanup.
            var existing = Referenced(privateKeyRef: "pk-ref");
            _repo.SetupSequence(r => r.GetIdentityProviderByIdAsync("idp-1"))
                .ReturnsAsync(existing)
                .ThrowsAsync(new InvalidOperationException("read failed cs-ref"));
            _repo.Setup(r => r.DeleteIdentityProviderAsync("idp-1")).Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid")).ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "cid" });
            _repo.Setup(r => r.DeleteOidcClientAsync(It.IsAny<DeleteOIDCClientRequest>())).Returns(Task.CompletedTask);
            var logger = new TestDoubles.CapturingLogger<AuthenticationDomainService>();

            var result = await CreateWithLogger(logger).DeleteIdentityProviderAsync("idp-1");

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.DeleteOidcClientAsync(It.Is<DeleteOIDCClientRequest>(d => d.ItemId == "cid")), Times.Once);
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _secrets.VerifyNoOtherCalls();
            logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning)
                .Which.AllText.Should().Contain("google").And.Contain(nameof(InvalidOperationException))
                .And.NotContain("cs-ref").And.NotContain("pk-ref").And.NotContain("read failed");
        }

        [Fact]
        public async Task DeleteOidcClient_RelatedProviderWithReference_RetiresItsSecret()
        {
            // H11 at the OIDC-client delete site (shared helper)
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(new OidcClientRegistration { ItemId = "c1", ClientId = "cid" });
            _repo.Setup(r => r.DeleteOidcClientAsync(It.IsAny<DeleteOIDCClientRequest>())).Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetIdentityProvidersAsync()).ReturnsAsync(new List<IdentityProvider> { Referenced() });
            _repo.Setup(r => r.DeleteIdentityProviderAsync("idp-1")).Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync((IdentityProvider)null!);
            _secrets.Setup(s => s.DeleteAsync("cs-ref", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            (await Create().DeleteOidcClientAsync(new DeleteOIDCClientRequest { ItemId = "c1" })).IsSuccess.Should().BeTrue();

            _secrets.Verify(s => s.DeleteAsync("cs-ref", It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetIdentityProviders_NeverReturnReferences()
        {
            // H12 (domain side; JSON side in SecretFieldsJsonModifierTests)
            _repo.Setup(r => r.GetIdentityProvidersForViewAsync()).ReturnsAsync(new List<IdentityProvider> { Referenced(privateKeyRef: "pk-ref") });
            _repo.Setup(r => r.GetIdentityProviderByIdForViewAsync("idp-1")).ReturnsAsync(Referenced(privateKeyRef: "pk-ref"));

            var all = await Create().GetAllIdentityProvidersAsync();
            var one = await Create().GetIdentityProviderByIdAsync("idp-1");

            all.Should().OnlyContain(p => p.ClientSecretReference == null && p.PrivateKeyReference == null);
            one!.ClientSecretReference.Should().BeNull();
            one.PrivateKeyReference.Should().BeNull();
            _secrets.VerifyNoOtherCalls();
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                const string json = @"{
                    ""issuer"": ""https://test.com/"",
                    ""authorization_endpoint"": ""https://test.com/auth"",
                    ""token_endpoint"": ""https://test.com/token""
                }";
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });
            }
        }
    }
}
