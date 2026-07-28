using Authentication.DomainService.Entities;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.RequestModel;
using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Users;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Net.Http;

namespace XUnitTest.Auth.Shared
{
    public class AuthenticationDomainServiceTests : IDisposable
    {
        private readonly Mock<IMessageClient> _message = new();
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IConfiguration> _config = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IValidator<SaveSsoCredentialRequest>> _ssoValidator = new();
        private readonly Mock<IValidator<SaveOIDCClientRequest>> _oidcValidator = new();
        private readonly Mock<IValidator<SaveIdentityProviderRequest>> _saveIdpValidator = new();
        private readonly Mock<IValidator<UpdateIdentityProviderRequest>> _updateIdpValidator = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IHttpClientFactory> _httpFactory = new();

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
            _saveIdpValidator.Setup(v => v.ValidateAsync(It.IsAny<SaveIdentityProviderRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
            _updateIdpValidator.Setup(v => v.ValidateAsync(It.IsAny<UpdateIdentityProviderRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private AuthenticationDomainService Create() =>
            new(_message.Object, _repo.Object, _config.Object, _userRepo.Object, _ssoValidator.Object,
                _oidcValidator.Object, _saveIdpValidator.Object, _updateIdpValidator.Object, _tenants.Object, _httpFactory.Object);

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
            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest());
            result.Errors.Should().ContainKey("not_found");
        }

        [Fact]
        public async Task UpdateIdp_ImmutableProvider_ReturnsError()
        {
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(Idp());
            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { Provider = "other" });
            result.Errors.Should().ContainKey("immutable_field");
        }

        [Fact]
        public async Task UpdateIdp_ImmutableClientId_ReturnsError()
        {
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(Idp());
            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ClientId = "different" });
            result.Errors.Should().ContainKey("immutable_field");
        }

        [Fact]
        public async Task UpdateIdp_HappyPath_PartialMerge()
        {
            var existing = Idp();
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
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
            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { ProviderType = "enterprise" });
            result.Errors.Should().ContainKey("immutable_field");
        }

        [Fact]
        public async Task UpdateIdp_ImmutableProtocol_ReturnsError()
        {
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(Idp());
            var result = await Create().UpdateIdentityProviderAsync("idp-1", new UpdateIdentityProviderRequest { Protocol = "saml" });
            result.Errors.Should().ContainKey("immutable_field");
        }

        [Fact]
        public async Task UpdateIdp_WellKnownUrlChanged_DiscoveryInvalid_ReturnsErrors()
        {
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(Idp());
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

            var result = await Create().UpdateIdentityProviderAsync("idp-1", request);

            result.IsSuccess.Should().BeTrue();
            existing.Issuer.Should().Be("https://issuer");
            existing.WellKnownUrl.Should().Be("https://new.example/.well-known/openid-configuration");
            existing.RequirePkce.Should().BeTrue();
            existing.AppleAudience.Should().Be("aud");
            existing.InitialRoles.Should().Contain("role-1");
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
            var result = await Create().DeleteIdentityProviderAsync("idp-1");
            result.Errors.Should().ContainKey("not_found");
        }

        [Fact]
        public async Task DeleteIdp_HappyPath_DeletesAndCleansUpOidcClient()
        {
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(Idp());
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
            var result = await Create().UpdateIdentityProviderStatusAsync("idp-1", true);
            result.Errors.Should().ContainKey("not_found");
        }

        [Fact]
        public async Task UpdateStatus_HappyPath_SetsActive()
        {
            var existing = Idp();
            existing.IsActive = false;
            _repo.Setup(r => r.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(existing);
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
            _repo.Setup(r => r.GetIdentityProvidersAsync()).ReturnsAsync(new List<IdentityProvider> { Idp() });

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
            var result = await Create().RotateOidcClientSecretAsync("c1");
            result.IsSuccess.Should().BeTrue();
            result.ClientSecret.Should().NotBe("old");
            result.ClientSecret.Should().StartWith("blxsk_");
        }

        // ---------- GenerateUserCodeByClientAsync ----------

        [Fact]
        public async Task GenerateUserCode_EmptyClientId_ReturnsError()
        {
            var result = await Create().GenerateUserCodeByClientAsync(new GenerateUserCodeRequest { ClientId = "" });
            result.Errors.Should().ContainKey("invalid_request");
        }

        [Fact]
        public async Task GenerateUserCode_HappyPath_Saves()
        {
            _repo.Setup(r => r.SaveUserCodeByClientAsync(It.IsAny<UserCode>())).Returns(Task.CompletedTask);
            var result = await Create().GenerateUserCodeByClientAsync(new GenerateUserCodeRequest { ClientId = "c1", Note = "n" });
            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.SaveUserCodeByClientAsync(It.Is<UserCode>(u => u.ClientId == "c1")), Times.Once);
        }

        // ---------- SaveClientCredentialAsync ----------

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
            _repo.Setup(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>())).ReturnsAsync(new BaseResponse { IsSuccess = true });
            var result = await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest { Name = "svc", Roles = new() { "r" }, Permissions = new() { "p" } });
            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.SaveClientCredentialAsync(It.Is<ClientCredential>(c => c.Name == "svc")), Times.Once);
        }

        [Fact]
        public async Task SaveClientCredential_Update_HappyPath()
        {
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync(new ClientCredential { ItemId = "cc-1", Name = "old" });
            _repo.Setup(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>())).ReturnsAsync(new BaseResponse { IsSuccess = true });
            var result = await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest { ItemId = "cc-1", Name = "new", Roles = new() { "r" } });
            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.SaveClientCredentialAsync(It.Is<ClientCredential>(c => c.Name == "new")), Times.Once);
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
            _repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>())).ReturnsAsync((OidcClientRegistration)null!);
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>())).Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync((IdentityProvider)null!);

            var result = await Create().SaveOIDCClientAsync(new SaveOIDCClientRequest
            {
                RedirectUris = new() { "https://app/cb" },
                AllowedScopes = new() { "openid" },
                ClientType = "public",
                RegisterAsIdentityProvider = false
            });

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()), Times.Once);
            _repo.Verify(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
        }

        [Fact]
        public async Task SaveOIDCClient_RegisterAsProvider_CreatesProvider()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>())).ReturnsAsync((OidcClientRegistration)null!);
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>())).Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync((IdentityProvider)null!);
            _repo.Setup(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(Idp());

            var result = await Create().SaveOIDCClientAsync(new SaveOIDCClientRequest
            {
                RedirectUris = new() { "https://app/cb" },
                AllowedScopes = new() { "openid" },
                ClientType = "confidential",
                ClientDisplayName = "My App",
                RegisterAsIdentityProvider = true
            });

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Once);
        }

        [Fact]
        public async Task DeleteOidcClient_HappyPath_CleansUpProvider()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(new OidcClientRegistration { ItemId = "c1", ClientId = "c1" });
            _repo.Setup(r => r.DeleteOidcClientAsync(It.IsAny<DeleteOIDCClientRequest>())).Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetIdentityProvidersAsync()).ReturnsAsync(new List<IdentityProvider> { Idp(clientId: "c1") });
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

        // ---------- GetClientCredentialsAsync ----------

        [Fact]
        public async Task GetClientCredentials_ReturnsRepositoryResult()
        {
            var credentials = new List<ClientCredential> { new() { ItemId = "cc-1" } };
            _repo.Setup(r => r.GetClientCredentialsAsync()).ReturnsAsync(credentials);

            var result = await Create().GetClientCredentialsAsync(new GetAllClientCredentialsRequest());

            result.Should().HaveCount(1);
            result[0].ItemId.Should().Be("cc-1");
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
