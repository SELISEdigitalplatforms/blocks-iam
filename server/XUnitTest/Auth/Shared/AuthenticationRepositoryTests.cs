using Authentication.DomainService.Entities;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// Unit tests for <see cref="AuthenticationRepository"/>. The repository resolves collections through
    /// <see cref="IDbContextProvider"/>, mocked here to hand back in-memory <see cref="IMongoCollection{T}"/>
    /// instances so filter construction, lockout backoff logic and result mapping are covered without MongoDB.
    /// </summary>
    public sealed class AuthenticationRepositoryTests : IDisposable
    {
        private readonly Mock<IDbContextProvider> _db = new();
        private readonly Mock<IHttpClientFactory> _httpFactory = new();
        private readonly Mock<IKeyValueStore> _keyValueStore = new();

        public AuthenticationRepositoryTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private AuthenticationRepository Sut() =>
            new(_db.Object, new OidcDiscoveryClient(_httpFactory.Object), _keyValueStore.Object);

        private Mock<IMongoCollection<T>> Register<T>(IEnumerable<T>? items = null)
        {
            var col = MongoMock.Collection(items);
            _db.Setup(d => d.GetCollection<T>(It.IsAny<string>())).Returns(col.Object);
            _db.Setup(d => d.GetCollection<T>(It.IsAny<string>(), It.IsAny<string>())).Returns(col.Object);
            return col;
        }

        private static IdentityProvider Idp(string id = "idp1", string provider = "custom",
            string providerType = "custom", string clientId = "c1", string? wellKnown = null) =>
            new()
            {
                ItemId = id,
                Provider = provider,
                ProviderType = providerType,
                ClientId = clientId,
                ClientSecret = "secret",
                TokenEndpointAuthMethod = "client_secret_post",
                WellKnownUrl = wellKnown,
                RedirectUris = new List<string> { "https://app/callback" }
            };

        private static User MakeUser(string id = "u1") => new() { ItemId = id, Email = "user@x.com", UserName = "user" };

        [Fact]
        public async Task GetUserByEmailAsync_NormalizesAndReturnsMatch()
        {
            Register(new[] { MakeUser() });
            (await Sut().GetUserByEmailAsync("  USER@X.com ")).ItemId.Should().Be("u1");
        }

        [Fact]
        public async Task GetUserByUsernameAsync_WithOrganization_ReturnsMatch()
        {
            Register(new[] { MakeUser() });
            (await Sut().GetUserByUsernameAsync("USER", "org1")).ItemId.Should().Be("u1");
        }

        [Fact]
        public async Task GetUserByUsernameAsync_DefaultOrg_SkipsOrgFilter()
        {
            Register(new[] { MakeUser() });
            (await Sut().GetUserByUsernameAsync("user", "default")).ItemId.Should().Be("u1");
        }

        [Fact]
        public async Task GetUserByIdAsync_ReturnsMatch()
        {
            Register(new[] { MakeUser() });
            (await Sut().GetUserByIdAsync("u1")).ItemId.Should().Be("u1");
        }

        [Fact]
        public async Task IncrementFailedLoginAsync_ReturnsNull_WhenUserMissing()
        {
            var col = Register<User>();
            MongoMock.SetupFindOneAndUpdate(col, (User?)null);
            (await Sut().IncrementFailedLoginAndApplyLockoutAsync("u1", 5, 5, DateTime.UtcNow)).Should().BeNull();
        }

        [Fact]
        public async Task IncrementFailedLoginAsync_BelowThreshold_ReturnsUser_NoLock()
        {
            var col = Register<User>();
            var user = new User { ItemId = "u1", FailedLoginCount = 2 };
            MongoMock.SetupFindOneAndUpdate(col, user);
            var result = await Sut().IncrementFailedLoginAndApplyLockoutAsync("u1", 5, 5, DateTime.UtcNow);
            result!.LockoutUntilUtc.Should().BeNull();
        }

        [Fact]
        public async Task IncrementFailedLoginAsync_AlreadyLocked_ReturnsUser()
        {
            var now = DateTime.UtcNow;
            var col = Register<User>();
            var user = new User { ItemId = "u1", FailedLoginCount = 5, LockoutUntilUtc = now.AddMinutes(10) };
            MongoMock.SetupFindOneAndUpdate(col, user);
            var result = await Sut().IncrementFailedLoginAndApplyLockoutAsync("u1", 5, 5, now);
            result!.LockoutUntilUtc.Should().Be(now.AddMinutes(10));
        }

        [Fact]
        public async Task IncrementFailedLoginAsync_AtThreshold_AppliesLockout()
        {
            var now = DateTime.UtcNow;
            var col = Register<User>();
            var afterIncrement = new User { ItemId = "u1", FailedLoginCount = 5, LockoutCount = 0 };
            var afterLock = new User { ItemId = "u1", FailedLoginCount = 5, LockoutUntilUtc = now.AddMinutes(5) };
            MongoMock.SetupFindOneAndUpdate(col, afterIncrement, afterLock);
            var result = await Sut().IncrementFailedLoginAndApplyLockoutAsync("u1", 5, 5, now);
            result!.LockoutUntilUtc.Should().Be(now.AddMinutes(5));
        }

        [Fact]
        public async Task IncrementFailedMfaAsync_AtThreshold_AppliesLockout()
        {
            var now = DateTime.UtcNow;
            var col = Register<User>();
            var afterIncrement = new User { ItemId = "u1", FailedMfaCount = 5, LockoutCount = 1 };
            var afterLock = new User { ItemId = "u1", FailedMfaCount = 5, LockoutUntilUtc = now.AddMinutes(15) };
            MongoMock.SetupFindOneAndUpdate(col, afterIncrement, afterLock);
            var result = await Sut().IncrementFailedMfaAndApplyLockoutAsync("u1", 5, 5, now);
            result!.LockoutUntilUtc.Should().Be(now.AddMinutes(15));
        }

        [Fact]
        public async Task IncrementFailedMfaAsync_ReturnsNull_WhenUserMissing()
        {
            var col = Register<User>();
            MongoMock.SetupFindOneAndUpdate(col, (User?)null);
            (await Sut().IncrementFailedMfaAndApplyLockoutAsync("u1", 5, 5, DateTime.UtcNow)).Should().BeNull();
        }

        [Fact]
        public async Task GetIdentityProvidersAsync_ReturnsList()
        {
            Register(new[] { Idp() });
            (await Sut().GetIdentityProvidersAsync()).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetIdentityProviderByProvider_ReturnsMatch()
        {
            Register(new[] { Idp() });
            (await Sut().GetIdentityProviderAsync("custom"))!.ItemId.Should().Be("idp1");
        }

        [Fact]
        public async Task GetIdentityProviderByProviderAndType_ReturnsMatch()
        {
            Register(new[] { Idp() });
            (await Sut().GetIdentityProviderAsync("custom", "custom"))!.ItemId.Should().Be("idp1");
        }

        [Fact]
        public async Task GetIdentityProviderByClientId_ReturnsMatch()
        {
            Register(new[] { Idp() });
            (await Sut().GetIdentityProviderByClientIdAsync("c1"))!.ItemId.Should().Be("idp1");
        }

        [Fact]
        public async Task GetIdentityProviderByClientIdAndRedirectUri_ReturnsMatch()
        {
            Register(new[] { Idp() });
            (await Sut().GetIdentityProviderByClientIdAndRedirectUriAsync("c1", "https://app/callback"))!.ItemId.Should().Be("idp1");
        }

        [Fact]
        public async Task GetIdentityProviderById_ReturnsMatch()
        {
            Register(new[] { Idp() });
            (await Sut().GetIdentityProviderByIdAsync("idp1"))!.ItemId.Should().Be("idp1");
        }

        [Fact]
        public async Task CreateIdentityProviderAsync_SocialProvider_PopulatesSocialMetadata()
        {
            var col = Register<IdentityProvider>();
            var provider = Idp(provider: "google");
            var result = await Sut().CreateIdentityProviderAsync(provider);
            result.ItemId.Should().NotBeNullOrEmpty();
            result.TokenUrl.Should().Contain("googleapis.com");
            col.Verify(c => c.InsertOneAsync(It.IsAny<IdentityProvider>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CreateIdentityProviderAsync_UnknownSocial_LeavesEndpointsNull()
        {
            Register<IdentityProvider>();
            var result = await Sut().CreateIdentityProviderAsync(Idp(provider: "unknown"));
            result.TokenUrl.Should().BeNull();
        }

        [Fact]
        public async Task CreateIdentityProviderAsync_WellKnownUrl_PopulatesFromDiscovery()
        {
            var handler = new StubHandler(
                "{\"issuer\":\"https://idp/\",\"authorization_endpoint\":\"https://idp/auth\",\"token_endpoint\":\"https://idp/token\"}");
            _httpFactory.Setup(f => f.CreateClient(OidcDiscoveryClient.HttpClientName))
                .Returns(new HttpClient(handler) { BaseAddress = new Uri("https://idp/") });
            Register<IdentityProvider>();
            var result = await Sut().CreateIdentityProviderAsync(Idp(wellKnown: "https://idp/.well-known/openid-configuration"));
            result.TokenUrl.Should().Be("https://idp/token");
            result.Issuer.Should().Be("https://idp/");
        }

        [Fact]
        public async Task UpdateIdentityProviderAsync_SetsAuditFieldsAndReplaces()
        {
            var col = Register<IdentityProvider>();
            var result = await Sut().UpdateIdentityProviderAsync(Idp());
            result.LastUpdatedBy.Should().Be("actor-1");
            col.Verify(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<IdentityProvider>>(), It.IsAny<IdentityProvider>(),
                It.IsAny<ReplaceOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteIdentityProviderAsync_DeletesOne()
        {
            var col = Register<IdentityProvider>();
            await Sut().DeleteIdentityProviderAsync("idp1");
            col.Verify(c => c.DeleteOneAsync(It.IsAny<FilterDefinition<IdentityProvider>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdatePartialAsync_DefaultCollection_UpdatesOne()
        {
            var col = Register<User>();
            await Sut().UpdatePartialAsync<User>("u1", new Dictionary<string, object> { { "Email", "z@x.com" } });
            col.Verify(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<User>>(), It.IsAny<UpdateDefinition<User>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdatePartialAsync_NamedCollection_UpdatesOne()
        {
            var col = Register<User>();
            await Sut().UpdatePartialAsync<User>("u1", new Dictionary<string, object> { { "Email", "z@x.com" } }, "Users");
            col.Verify(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<User>>(), It.IsAny<UpdateDefinition<User>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetAuthenticationConfigurationAsync_ReturnsMatch()
        {
            var id = ObjectId.GenerateNewId();
            Register(new[] { new IdentityConfiguration { ItemId = id } });
            (await Sut().GetAuthenticationConfigurationAsync()).ItemId.Should().Be(id);
        }

        [Fact]
        public async Task UpdateAuthenticationConfigurationAsync_Replaces()
        {
            var col = Register<IdentityConfiguration>();
            await Sut().UpdateAuthenticationConfigurationAsync(new IdentityConfiguration { ItemId = ObjectId.GenerateNewId() });
            col.Verify(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<IdentityConfiguration>>(), It.IsAny<IdentityConfiguration>(),
                It.IsAny<ReplaceOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetOidcClientRegistrationAsync_ReturnsMatch()
        {
            Register(new[] { new OidcClientRegistration { ItemId = "oc1" } });
            (await Sut().GetOidcClientRegistrationAsync("oc1")).ItemId.Should().Be("oc1");
        }

        [Fact]
        public async Task SaveOidcClientRegistrationAsync_Upserts()
        {
            var col = Register<OidcClientRegistration>();
            await Sut().SaveOidcClientRegistrationAsync(new OidcClientRegistration { ItemId = "oc1" });
            col.Verify(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<OidcClientRegistration>>(), It.IsAny<OidcClientRegistration>(),
                It.Is<ReplaceOptions>(o => o.IsUpsert), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetOidcUiTemplateAsync_ReturnsStoredTemplate()
        {
            var stored = new OidcUiTemplate
            {
                Branding = new OidcUiTemplateBranding { BrandName = "Acme" }
            };
            _keyValueStore
                .Setup(s => s.GetAsync<OidcUiTemplate>(AuthenticationRepository.OidcUiTemplateStoreKey))
                .ReturnsAsync(stored);

            var result = await Sut().GetOidcUiTemplateAsync();

            result.Should().BeSameAs(stored);
            _keyValueStore.Verify(
                s => s.GetAsync<OidcUiTemplate>("oidcUiTemplate"),
                Times.Once);
        }

        [Fact]
        public async Task GetOidcUiTemplateAsync_ReturnsNull_WhenStoreHasNoEntry()
        {
            _keyValueStore
                .Setup(s => s.GetAsync<OidcUiTemplate>(It.IsAny<string>()))
                .ReturnsAsync((OidcUiTemplate?)null);

            var result = await Sut().GetOidcUiTemplateAsync();

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetOidcUiTemplateAsync_PropagatesStoreFailure()
        {
            _keyValueStore
                .Setup(s => s.GetAsync<OidcUiTemplate>(It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("store unavailable"));

            var action = () => Sut().GetOidcUiTemplateAsync();

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("store unavailable");
        }

        [Fact]
        public async Task SaveOidcUiTemplateAsync_UsesSingletonStoreKeyAndSetAsync()
        {
            var template = new OidcUiTemplate
            {
                Branding = new OidcUiTemplateBranding { BrandName = "Acme" }
            };

            await Sut().SaveOidcUiTemplateAsync(template);

            _keyValueStore.Verify(
                s => s.SetAsync("oidcUiTemplate", template),
                Times.Once);
        }

        [Fact]
        public async Task GetOIDCCredentialByIdAsync_ReturnsMatch()
        {
            Register(new[] { new OidcClientRegistration { ItemId = "oc1" } });
            (await Sut().GetOIDCCredentialByIdAsync("oc1")).ItemId.Should().Be("oc1");
        }

        [Fact]
        public async Task GetOIDCCredentialsByTenantAsync_ReturnsList()
        {
            Register(new[] { new OidcClientRegistration { ItemId = "oc1" } });
            (await Sut().GetOIDCCredentialsByTenantAsync()).Should().HaveCount(1);
        }

        [Fact]
        public async Task DeleteOidcClientAsync_DeletesOne()
        {
            var col = Register<OidcClientRegistration>();
            await Sut().DeleteOidcClientAsync(new DeleteOIDCClientRequest { ItemId = "oc1" });
            col.Verify(c => c.DeleteOneAsync(It.IsAny<FilterDefinition<OidcClientRegistration>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetClientCredentialByIdAsync_ReturnsMatch()
        {
            Register(new[] { new ClientCredential { ItemId = "cc1" } });
            (await Sut().GetClientCredentialByIdAsync("cc1")).ItemId.Should().Be("cc1");
        }

        [Fact]
        public async Task AuthenticateBiometricCredentialAsync_ReturnsMatch()
        {
            Register(new[] { new BiometricCredential { ItemId = "b1", BiometricId = "bid", BiometricKey = "bkey" } });
            (await Sut().AuthenticateBiometricCredentialAsync("bid", "bkey")).ItemId.Should().Be("b1");
        }

        [Fact]
        public async Task GetUserCodeAsync_ReturnsMatch()
        {
            Register(new[] { new UserCode { ItemId = "uc1", Code = "123" } });
            (await Sut().GetUserCodeAsync("123")).ItemId.Should().Be("uc1");
        }

        [Fact]
        public async Task GetBlocksClientAsync_ReturnsMatch()
        {
            Register(new[] { new BlocksClientConfig { ItemId = "bc1" } });
            (await Sut().GetBlocksClientAsync("bc1")).ItemId.Should().Be("bc1");
        }

        [Fact]
        public async Task SaveUserCodeByClientAsync_Upserts()
        {
            var col = Register<UserCode>();
            await Sut().SaveUserCodeByClientAsync(new UserCode { ItemId = "uc1" });
            col.Verify(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<UserCode>>(), It.IsAny<UserCode>(),
                It.Is<ReplaceOptions>(o => o.IsUpsert), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetUserCodesByUserIdAsync_MapsExpiryFromTtl()
        {
            var created = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Register(new[]
            {
                new UserCode { ItemId = "uc1", UserId = "u1", Code = "c", CodeTtlInMinute = 10, CreatedDate = created },
                new UserCode { ItemId = "uc2", UserId = "u1", Code = "d", CodeTtlInMinute = null, CreatedDate = created }
            });
            var result = await Sut().GetUserCodesByUserIdAsync("u1");
            result.Should().HaveCount(2);
            result.Single(r => r.ItemId == "uc1").ExpiryDate.Should().Be(created.AddMinutes(10));
            result.Single(r => r.ItemId == "uc2").ExpiryDate.Should().Be(created);
        }

        [Fact]
        public async Task SaveClientCredentialAsync_ReturnsSuccess()
        {
            Register<ClientCredential>();
            (await Sut().SaveClientCredentialAsync(new ClientCredential { ItemId = "cc1" })).IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task DeleteClientCredentialAsync_DeletesOne()
        {
            var col = Register<ClientCredential>();
            await Sut().DeleteClientCredentialAsync(new DeleteClientCredentialRequest { ItemId = "cc1" });
            col.Verify(c => c.DeleteOneAsync(It.IsAny<FilterDefinition<ClientCredential>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetClientCredentialsAsync_ReturnsList()
        {
            Register(new[] { new ClientCredential { ItemId = "cc1" } });
            (await Sut().GetClientCredentialsAsync(null)).Should().HaveCount(1);
        }

        // The collection mock hands back its seeded items whatever the filter says, so a
        // seeded-document assertion would pass even with no organization clause at all. The
        // rendered filter is the only thing that actually proves the scoping reached the database.

        [Fact]
        public async Task GetClientCredentialsAsync_ScopedToOrganization_FiltersOnOrganizationId()
        {
            var col = Register(new[] { new ClientCredential { ItemId = "cc1", OrganizationId = "org-a" } });

            await Sut().GetClientCredentialsAsync("org-a");

            col.Verify(c => c.FindAsync(
                It.Is<FilterDefinition<ClientCredential>>(f => FiltersOnOrganization(f, "org-a")),
                It.IsAny<FindOptions<ClientCredential, ClientCredential>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetClientCredentialsAsync_TenantWide_DoesNotFilterOnOrganization()
        {
            var col = Register(new[] { new ClientCredential { ItemId = "cc1", OrganizationId = "org-a" } });

            await Sut().GetClientCredentialsAsync(null);

            col.Verify(c => c.FindAsync(
                It.Is<FilterDefinition<ClientCredential>>(f => !RenderFilter(f).Contains("OrganizationId")),
                It.IsAny<FindOptions<ClientCredential, ClientCredential>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        private static BsonDocument RenderFilter<T>(FilterDefinition<T> filter)
        {
            var registry = MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry;
            return filter.Render(new RenderArgs<T>(registry.GetSerializer<T>(), registry));
        }

        private static bool FiltersOnOrganization<T>(FilterDefinition<T> filter, string organizationId) =>
            RenderFilter(filter).TryGetValue("OrganizationId", out var value)
            && value.IsString
            && value.AsString == organizationId;

        [Fact]
        public async Task InsertImpersonationSessionAsync_ReturnsTrue()
        {
            var col = Register<ImpersonationSession>();
            (await Sut().InsertImpersonationSessionAsync(new ImpersonationSession { UserId = "u1" })).Should().BeTrue();
            col.Verify(c => c.InsertOneAsync(It.IsAny<ImpersonationSession>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task InsertImpersonationSessionAsync_ReturnsFalse_OnError()
        {
            _db.Setup(d => d.GetCollection<ImpersonationSession>(It.IsAny<string>())).Throws(new InvalidOperationException("boom"));
            (await Sut().InsertImpersonationSessionAsync(new ImpersonationSession { UserId = "u1" })).Should().BeFalse();
        }

        [Fact]
        public async Task GetImpersonationSessionByIdAsync_ReturnsMatch()
        {
            Register(new[] { new ImpersonationSession { Id = "s1", UserId = "u1" } });
            (await Sut().GetImpersonationSessionByIdAsync("s1"))!.Id.Should().Be("s1");
        }

        [Fact]
        public async Task GetActiveImpersonationSessionsByUserIdAsync_ReturnsList()
        {
            Register(new[] { new ImpersonationSession { Id = "s1", UserId = "u1", Status = "active" } });
            (await Sut().GetActiveImpersonationSessionsByUserIdAsync("u1")).Should().HaveCount(1);
        }

        [Fact]
        public async Task UpdateImpersonationSessionAsync_ReturnsTrue_WhenModified()
        {
            Register<ImpersonationSession>();
            var result = await Sut().UpdateImpersonationSessionAsync("s1", new Dictionary<string, object> { { "Status", "ended_by_admin_stop" } });
            result.Should().BeTrue();
        }

        [Fact]
        public async Task UpdateImpersonationSessionAsync_ReturnsFalse_OnError()
        {
            _db.Setup(d => d.GetCollection<ImpersonationSession>(It.IsAny<string>())).Throws(new InvalidOperationException("boom"));
            (await Sut().UpdateImpersonationSessionAsync("s1", new Dictionary<string, object>())).Should().BeFalse();
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly string _json;
            public StubHandler(string json) => _json = json;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(_json)
                });
        }
    }
}
