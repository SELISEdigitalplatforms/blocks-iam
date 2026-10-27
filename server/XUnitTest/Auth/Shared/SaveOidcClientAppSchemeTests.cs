using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Blocks.Genesis;
using Blocks.Genesis.DataAccess.Rules;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using System.Net.Http;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// <see cref="AuthenticationDomainService.SaveOIDCClientAsync"/> with the real
    /// <see cref="SaveOIDCClientRequestValidator"/>: mobile app-scheme redirect URIs end to end
    /// through the domain service, down to what reaches the repository.
    /// </summary>
    public class SaveOidcClientAppSchemeTests : IDisposable
    {
        private const string AppRedirectUri = "com.klaxai.app://login/callback";

        private readonly Mock<IMessageClient> _message = new();
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IValidator<SaveOidcUiTemplateRequest>> _oidcUiTemplateValidator = new();
        private readonly Mock<IValidator<SaveIdentityProviderRequest>> _saveIdpValidator = new();
        private readonly Mock<IValidator<UpdateIdentityProviderRequest>> _updateIdpValidator = new();
        private readonly Mock<IHttpClientFactory> _httpFactory = new();

        public SaveOidcClientAppSchemeTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
            _repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>())).ReturnsAsync((OidcClientRegistration)null!);
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync((IdentityProvider)null!);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private AuthenticationDomainService Create() =>
            new(_message.Object, _repo.Object,
                new AuthenticationRequestValidators(new SaveOIDCClientRequestValidator(), _oidcUiTemplateValidator.Object, _saveIdpValidator.Object,
                    _updateIdpValidator.Object), _httpFactory.Object, TestDoubles.IdentityProviderSecretTestDoubles.Resolver());

        private static SaveOIDCClientRequest AppClient() => new()
        {
            ClientType = "public",
            RequirePkce = true,
            RedirectUris = new List<string> { AppRedirectUri },
            AllowedScopes = new List<string> { "openid" }
        };

        private void VerifyNoIdentityProviderWrite()
        {
            _repo.Verify(r => r.CreateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
            _repo.Verify(r => r.UpdateIdentityProviderAsync(It.IsAny<IdentityProvider>()), Times.Never);
            _repo.Verify(r => r.DeleteIdentityProviderAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Save_PublicPkceAppSchemeClient_StoresTheUriExactly()
        {
            // H1 / Example 1
            OidcClientRegistration? saved = null;
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()))
                .Callback<OidcClientRegistration>(credential => saved = credential)
                .Returns(Task.CompletedTask);

            var result = await Create().SaveOIDCClientAsync(AppClient());

            result.IsSuccess.Should().BeTrue();
            saved!.RedirectUris.Should().Equal(AppRedirectUri);
            saved.TokenEndpointAuthMethod.Should().Be("none");
            saved.RequirePkce.Should().BeTrue();
        }

        [Fact]
        public async Task Save_MixedRedirects_StoresBothInOrder()
        {
            // H2
            OidcClientRegistration? saved = null;
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()))
                .Callback<OidcClientRegistration>(credential => saved = credential)
                .Returns(Task.CompletedTask);
            var request = AppClient();
            request.RedirectUris = new List<string> { "https://app.example.com/cb", "com.example.app:/oauth2redirect" };
            request.PostLogoutRedirectUris = new List<string> { "com.example.app:/logout" };

            var result = await Create().SaveOIDCClientAsync(request);

            result.IsSuccess.Should().BeTrue();
            saved!.RedirectUris.Should().Equal("https://app.example.com/cb", "com.example.app:/oauth2redirect");
            saved.PostLogoutRedirectUris.Should().Equal("com.example.app:/logout");
        }

        [Fact]
        public async Task Save_AppSchemeClientAsIdentityProvider_IsRejectedAndWritesNothing()
        {
            // C6
            var request = AppClient();
            request.RegisterAsIdentityProvider = true;

            var result = await Create().SaveOIDCClientAsync(request);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Equal(new Dictionary<string, string>
            {
                ["RegisterAsIdentityProvider"] = "A client with mobile app redirect URIs cannot be registered as an identity provider."
            });
            _repo.Verify(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()), Times.Never);
            VerifyNoIdentityProviderWrite();
        }

        [Fact]
        public async Task Save_DotlessScheme_IsRejectedAndWritesNothing()
        {
            // C1 / Example 2
            var request = AppClient();
            request.RedirectUris = new List<string> { "myapp://callback" };

            var result = await Create().SaveOIDCClientAsync(request);

            result.Errors.Should().Equal(new Dictionary<string, string>
            {
                ["RedirectUris"] = "Each redirect URI must be an http(s) URL or a mobile app URI whose scheme is a reverse domain name, such as com.example.app://callback."
            });
            _repo.Verify(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()), Times.Never);
        }

        [Fact]
        public async Task Save_DataAccessRuleFailsAfterValidatorPasses_PropagatesAndSkipsIdentityProvider()
        {
            // C17: the schema rules still run on the write once the request validator passes.
            var failure = new DataValidationResult();
            failure.AddError("ClientName", "ClientName is required", "Required");
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()))
                .ThrowsAsync(new DataValidationException(failure));

            var act = () => Create().SaveOIDCClientAsync(AppClient());

            await act.Should().ThrowAsync<DataValidationException>();
            _repo.Verify(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()), Times.Once);
            _repo.Verify(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>()), Times.Never);
            VerifyNoIdentityProviderWrite();
        }
    }
}
