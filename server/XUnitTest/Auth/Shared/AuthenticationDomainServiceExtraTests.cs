using Authentication.DomainService.Entities;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.RequestModel;
using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Moq;
using System.Net;

namespace XUnitTest.Auth.Shared
{
    // Extra coverage for AuthenticationDomainService branches NOT covered by
    // AuthenticationDomainServiceTests: GetVisitorsIpAddresses, GetRequestOriginHostName,
    // and the SaveOIDCClient provider update / unregister transitions.
    public class AuthenticationDomainServiceExtraTests : IDisposable
    {
        private readonly Mock<IMessageClient> _message = new();
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IValidator<SaveOIDCClientRequest>> _oidcValidator = new();
        private readonly Mock<IValidator<SaveOidcUiTemplateRequest>> _oidcUiTemplateValidator = new();
        private readonly Mock<IValidator<SaveIdentityProviderRequest>> _saveIdpValidator = new();
        private readonly Mock<IValidator<UpdateIdentityProviderRequest>> _updateIdpValidator = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IHttpClientFactory> _httpFactory = new();

        public AuthenticationDomainServiceExtraTests()
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
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private AuthenticationDomainService Create() =>
            new(_message.Object, _repo.Object,
                _oidcValidator.Object, _oidcUiTemplateValidator.Object, _saveIdpValidator.Object,
                _updateIdpValidator.Object, _tenants.Object, _httpFactory.Object);

        private static IdentityProvider Idp(string provider = "google", string clientId = "cid", string id = "idp-1") => new()
        {
            ItemId = id, Provider = provider, ProviderType = "social", ClientId = clientId,
            ClientSecret = "secret", TokenEndpointAuthMethod = "client_secret_post", Protocol = "oidc"
        };

        // ---------- GetVisitorsIpAddresses ----------

        [Fact]
        public void GetVisitorsIps_XForwardedForPresent_ReturnsAllTrimmed()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.1, 198.51.100.2 , 192.0.2.3";

            var ips = Create().GetVisitorsIpAddresses(ctx).ToList();

            ips.Should().Equal("203.0.113.1", "198.51.100.2", "192.0.2.3");
        }

        [Fact]
        public void GetVisitorsIps_NoForwardedHeader_UsesRemoteIpAddress()
        {
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.5");

            var ips = Create().GetVisitorsIpAddresses(ctx).ToList();

            ips.Should().ContainSingle().Which.Should().Be("192.168.1.5");
        }

        [Fact]
        public void GetVisitorsIps_NoForwardedHeader_NoRemoteIp_ReturnsEmptySequence()
        {
            var ctx = new DefaultHttpContext(); // RemoteIpAddress is null

            var ips = Create().GetVisitorsIpAddresses(ctx).ToList();

            ips.Should().BeEmpty();
        }

        // ---------- GetRequestOriginHostName ----------

        [Fact]
        public void GetOriginHost_OriginHeaderPresent_ReturnsHost()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers["Origin"] = "https://app.example.com:8443/some/path";

            Create().GetRequestOriginHostName(ctx).Should().Be("app.example.com");
        }

        [Fact]
        public void GetOriginHost_NoOrigin_FallsBackToReferer()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers["Referer"] = "https://referer.example.org/page?x=1";

            Create().GetRequestOriginHostName(ctx).Should().Be("referer.example.org");
        }

        [Fact]
        public void GetOriginHost_NeitherHeader_ReturnsEmpty()
        {
            Create().GetRequestOriginHostName(new DefaultHttpContext()).Should().BeEmpty();
        }

        // ---------- SaveOIDCClientAsync provider sync transitions ----------

        [Fact]
        public async Task SaveOIDCClient_RegisterAsProvider_ExistingProvider_UpdatesInsteadOfCreates()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>())).ReturnsAsync((OidcClientRegistration)null!);
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>())).Returns(Task.CompletedTask);
            var existing = Idp(id: "idp-existing", clientId: "stale-cid");
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(existing);
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>())).ReturnsAsync(existing);

            var result = await Create().SaveOIDCClientAsync(new SaveOIDCClientRequest
            {
                RedirectUris = new() { "https://app/cb" },
                AllowedScopes = new() { "openid", "profile" },
                ClientType = "confidential",
                ClientDisplayName = "My Portal App",
                RegisterAsIdentityProvider = true
            });

            result.IsSuccess.Should().BeTrue();
            // ProviderType is asserted here because this branch once wrote "blocks" while the
            // create branch wrote "blocks-oidc" — the value the Identity Provider gallery filters
            // on — so every re-save of an already-registered client silently hid it from the UI.
            _repo.Verify(r => r.UpdateIdentityProviderAsync(It.Is<IdentityProvider>(p =>
                p.Provider == "my-portal-app" && p.ProviderType == "blocks-oidc")), Times.Once);
            _repo.Verify(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
        }

        [Fact]
        public async Task SaveOIDCClient_RegisterAsProvider_CreateAndUpdateBranchesAgreeOnProviderType()
        {
            static SaveOIDCClientRequest Req() => new()
            {
                RedirectUris = new() { "https://app/cb" },
                AllowedScopes = new() { "openid" },
                ClientType = "confidential",
                ClientDisplayName = "My Portal App",
                RegisterAsIdentityProvider = true
            };

            _repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>())).ReturnsAsync((OidcClientRegistration)null!);
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>())).Returns(Task.CompletedTask);

            // First save: no linked provider yet, so the create branch runs.
            IdentityProvider? created = null;
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync((IdentityProvider)null!);
            _repo.Setup(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>()))
                .Callback<IdentityProvider>(p => created = p)
                .ReturnsAsync(Idp());
            (await Create().SaveOIDCClientAsync(Req())).IsSuccess.Should().BeTrue();

            // Second save: a linked provider exists, so the update branch runs.
            IdentityProvider? updated = null;
            var existing = Idp(id: "idp-existing", clientId: "stale-cid");
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(existing);
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>()))
                .Callback<IdentityProvider>(p => updated = p)
                .ReturnsAsync(existing);
            (await Create().SaveOIDCClientAsync(Req())).IsSuccess.Should().BeTrue();

            // The invariant the original bug violated: registering and re-saving must classify a
            // client identically. Asserting the two branches against each other catches a drift
            // that per-branch assertions can miss when only one of them is updated.
            created.Should().NotBeNull();
            updated.Should().NotBeNull();
            updated!.ProviderType.Should().Be(created!.ProviderType);
            created.ProviderType.Should().Be("blocks-oidc");
        }

        [Fact]
        public async Task SaveOIDCClient_UnregisterProvider_DeletesLinkedProvider()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>())).ReturnsAsync((OidcClientRegistration)null!);
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>())).Returns(Task.CompletedTask);
            var existing = Idp(id: "idp-existing");
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(existing);
            _repo.Setup(r => r.DeleteIdentityProviderAsync("idp-existing")).Returns(Task.CompletedTask);

            var result = await Create().SaveOIDCClientAsync(new SaveOIDCClientRequest
            {
                RedirectUris = new() { "https://app/cb" },
                AllowedScopes = new() { "openid" },
                ClientType = "public",
                RegisterAsIdentityProvider = false
            });

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.DeleteIdentityProviderAsync("idp-existing"), Times.Once);
            _repo.Verify(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
            _repo.Verify(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
        }

        [Fact]
        public async Task SaveOIDCClient_Unregister_NoExistingProvider_NoDelete()
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
            _repo.Verify(r => r.DeleteIdentityProviderAsync(It.IsAny<string>()), Times.Never);
        }

        // ---------- RotateOidcClientSecretAsync provider secret sync ----------

        private OidcClientRegistration RotatableClient(bool registerAsIdentityProvider) => new()
        {
            ItemId = "item-1",
            ClientId = "cid",
            ClientSecret = "old-secret",
            IsDeviceFlowClient = false,
            RegisterAsIdentityProvider = registerAsIdentityProvider
        };

        [Fact]
        public async Task RotateOidcClientSecret_RegisteredAsProvider_SyncsNewSecretToLinkedProvider()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("item-1")).ReturnsAsync(RotatableClient(true));
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>())).Returns(Task.CompletedTask);
            var linked = Idp(clientId: "cid");
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync("cid")).ReturnsAsync(linked);
            IdentityProvider? synced = null;
            _repo.Setup(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>()))
                .Callback<IdentityProvider>(p => synced = p)
                .ReturnsAsync(linked);

            var result = await Create().RotateOidcClientSecretAsync("item-1");

            result.IsSuccess.Should().BeTrue();
            result.ClientSecret.Should().NotBe("old-secret");
            // The linked provider stores its own copy of the secret. Rotation used to write only
            // OidcClientRegistrations, leaving federation presenting the dead secret at the token
            // endpoint with nothing surfaced to the operator.
            synced.Should().NotBeNull();
            synced!.ClientSecret.Should().Be(result.ClientSecret);
        }

        [Fact]
        public async Task RotateOidcClientSecret_NotRegisteredAsProvider_LeavesProvidersUntouched()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("item-1")).ReturnsAsync(RotatableClient(false));
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>())).Returns(Task.CompletedTask);

            var result = await Create().RotateOidcClientSecretAsync("item-1");

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>()), Times.Never);
            _repo.Verify(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
        }

        [Fact]
        public async Task RotateOidcClientSecret_NoLinkedProvider_StillRotatesWithoutUpdating()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("item-1")).ReturnsAsync(RotatableClient(true));
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>())).Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync("cid")).ReturnsAsync((IdentityProvider)null!);

            var result = await Create().RotateOidcClientSecretAsync("item-1");

            result.IsSuccess.Should().BeTrue();
            result.ClientSecret.Should().NotBe("old-secret");
            _repo.Verify(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
        }
    }
}
