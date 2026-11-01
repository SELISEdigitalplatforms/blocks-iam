using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Shared
{
    /// <summary>The OIDC client sign-in policy through the admin API: save, keep, validate, read.</summary>
    public sealed class OidcClientLoginPolicyAdminTests : IDisposable
    {
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IValidator<SaveOIDCClientRequest>> _oidcValidator = new();
        private OidcClientRegistration? _stored;

        public OidcClientLoginPolicyAdminTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
            _repo.Setup(r => r.GetIdentityProvidersAsync()).ReturnsAsync(
            [
                Provider("google-1", "social"),
                Provider("ms-1", "social"),
                Provider("okta-1", "byos")
            ]);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>()))
                .ReturnsAsync((string id) => _stored?.ItemId == id ? Copy(_stored) : null!);
            _repo.Setup(r => r.GetOIDCCredentialByIdAsync(It.IsAny<string>()))
                .ReturnsAsync((string id) => _stored?.ItemId == id ? WithoutPolicy(Copy(_stored)) : null!);
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()))
                .Callback<OidcClientRegistration>(c => _stored = Copy(c))
                .Returns(Task.CompletedTask);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private static IdentityProvider Provider(string id, string type) => new()
        {
            ItemId = id, Provider = "google", ProviderType = type, IsActive = true,
            ClientId = id, ClientSecret = "s", TokenEndpointAuthMethod = "client_secret_post"
        };

        private static OidcClientRegistration Copy(OidcClientRegistration client) =>
            MongoDB.Bson.Serialization.BsonSerializer.Deserialize<OidcClientRegistration>(MongoDB.Bson.BsonExtensionMethods.ToBson(client));

        // Like the real view read: the schema projection does not return the policy.
        private static OidcClientRegistration WithoutPolicy(OidcClientRegistration client)
        {
            client.LoginPolicy = null;
            return client;
        }

        private AuthenticationDomainService DomainService()
        {
            _oidcValidator.Setup(v => v.ValidateAsync(It.IsAny<SaveOIDCClientRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync((IdentityProvider)null!);
            return new AuthenticationDomainService(
                new Mock<IMessageClient>().Object, _repo.Object,
                new AuthenticationRequestValidators(_oidcValidator.Object,
                    new Mock<IValidator<SaveOidcUiTemplateRequest>>().Object,
                    new Mock<IValidator<SaveIdentityProviderRequest>>().Object,
                    new Mock<IValidator<UpdateIdentityProviderRequest>>().Object),
                new Mock<IHttpClientFactory>().Object,
                TestDoubles.IdentityProviderSecretTestDoubles.Resolver(), TestDoubles.ClientCredentialSecretTestDoubles.Resolver(), TestDoubles.OidcClientSecretTestDoubles.Resolver(XUnitTest.TestDoubles.OidcClientSecretTestDoubles.AcceptingStore()),
                NullLogger<AuthenticationDomainService>.Instance);
        }

        private static SaveOIDCClientRequest SaveRequest(OidcClientLoginPolicy? policy) => new()
        {
            ItemId = "cid",
            RedirectUris = ["https://app.example.com/cb"],
            AllowedScopes = ["openid"],
            LoginPolicy = policy
        };

        private static OidcClientLoginPolicy Selected(params string[] ids) => new()
        {
            Method = OidcClientLoginMethods.Default,
            Providers = OidcClientProviderModes.Selected,
            SelectedProviderIds = ids.ToList()
        };

        [Fact]
        public async Task Save_StoresThePolicy_AndGetReturnsIt()
        {
            var result = await DomainService().SaveOIDCClientAsync(SaveRequest(Selected("google-1", "google-1")));

            result.IsSuccess.Should().BeTrue();
            _stored!.LoginPolicy!.SelectedProviderIds.Should().Equal("google-1");
            var shown = await DomainService().GetOidcClientAsync("cid");
            shown.LoginPolicy!.Providers.Should().Be(OidcClientProviderModes.Selected);
            shown.LoginPolicy.SelectedProviderIds.Should().Equal("google-1");
        }

        [Fact]
        public async Task Save_WithoutPolicy_KeepsTheStoredOne()
        {
            await DomainService().SaveOIDCClientAsync(SaveRequest(new OidcClientLoginPolicy { Method = OidcClientLoginMethods.Password }));

            (await DomainService().SaveOIDCClientAsync(SaveRequest(null))).IsSuccess.Should().BeTrue();

            _stored!.LoginPolicy!.Method.Should().Be(OidcClientLoginMethods.Password);
        }

        [Fact]
        public async Task Save_DefaultProviders_KeepsNoList()
        {
            var policy = new OidcClientLoginPolicy { Method = OidcClientLoginMethods.Social, SelectedProviderIds = ["google-1"] };

            await DomainService().SaveOIDCClientAsync(SaveRequest(policy));

            _stored!.LoginPolicy!.SelectedProviderIds.Should().BeEmpty();
        }

        [Fact]
        public async Task NewClientWithoutPolicy_StoresNone()
        {
            (await DomainService().SaveOIDCClientAsync(SaveRequest(null))).IsSuccess.Should().BeTrue();

            _stored!.LoginPolicy.Should().BeNull();
            (await DomainService().GetOidcClientAsync("cid")).LoginPolicy.Should().BeNull();
        }

        public static TheoryData<OidcClientLoginPolicy, string> InvalidPolicies => new()
        {
            { new OidcClientLoginPolicy { Method = "Password" }, "LoginPolicy.Method" },
            { new OidcClientLoginPolicy { Providers = "some" }, "LoginPolicy.Providers" },
            { Selected(), "LoginPolicy.SelectedProviderIds" },
            { Selected("unknown"), "LoginPolicy.SelectedProviderIds" },
            { Selected("google-1", "okta-1"), "LoginPolicy.SelectedProviderIds" },
        };

        [Theory]
        [MemberData(nameof(InvalidPolicies))]
        public async Task Save_InvalidPolicy_IsRefused_AndNothingIsSaved(OidcClientLoginPolicy policy, string errorKey)
        {
            var result = await DomainService().SaveOIDCClientAsync(SaveRequest(policy));

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey(errorKey);
            _repo.Verify(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()), Times.Never);
        }

        [Fact]
        public async Task GetList_ReturnsThePoliciesFromTheFullDocuments()
        {
            var withPolicy = new OidcClientRegistration { ItemId = "a", ClientId = "a", LoginPolicy = new OidcClientLoginPolicy { Method = OidcClientLoginMethods.Social } };
            var without = new OidcClientRegistration { ItemId = "b", ClientId = "b" };
            _repo.Setup(r => r.GetOIDCCredentialsByTenantForViewAsync()).ReturnsAsync([WithoutPolicy(Copy(withPolicy)), Copy(without)]);
            _repo.Setup(r => r.GetOIDCCredentialsByTenantAsync()).ReturnsAsync([withPolicy, without]);

            var result = await DomainService().GetOidcClientsAsync();

            result.LoginPolicies.Keys.Should().Equal("a");
            result.LoginPolicies["a"].Method.Should().Be(OidcClientLoginMethods.Social);
        }
    }
}
