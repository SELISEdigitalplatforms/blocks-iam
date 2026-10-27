using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// blocks-iam#649 H4: the Blocks Secrets reference fields survive insert, replace and the
    /// repository's non-view reads on a real Mongo. Runs only when the local test Mongo answers.
    /// </summary>
    public sealed class AuthenticationRepositoryIdentityProviderRealMongoTests : IDisposable
    {
        private readonly RealMongo _mongo = new();

        public AuthenticationRepositoryIdentityProviderRealMongoTests()
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
            _mongo.Dispose();
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        // A social provider without WellKnownUrl takes the built-in metadata path: no HTTP discovery.
        private AuthenticationRepository Sut() =>
            new(_mongo.ProviderFor(), new OidcDiscoveryClient(Mock.Of<IHttpClientFactory>()), Mock.Of<IKeyValueStore>(),
                new XUnitTest.TestDoubles.PassThroughIamDataAccess());

        private static IdentityProvider Provider(string clientId, string? clientSecretReference, string? privateKeyReference) => new()
        {
            Provider = "google-" + clientId,
            ProviderType = "social",
            ClientId = clientId,
            ClientSecret = clientSecretReference == null ? "legacy-secret" : string.Empty,
            ClientSecretReference = clientSecretReference,
            PrivateKey = null,
            PrivateKeyReference = privateKeyReference,
            TokenEndpointAuthMethod = "client_secret_post",
        };

        private BsonDocument Raw(string itemId) =>
            _mongo.Database.GetCollection<BsonDocument>("IdentityProviders").Find(Builders<BsonDocument>.Filter.Eq("_id", itemId)).First();

        [RealMongoFact]
        public async Task ReferenceFields_SurviveInsertAndTheNonViewReads()
        {
            var created = await Sut().CreateIdentityProviderAsync(Provider("cid-ref", "cs-ref", "pk-ref"));

            var byId = await Sut().GetIdentityProviderByIdAsync(created.ItemId);
            var byClientId = await Sut().GetIdentityProviderByClientIdAsync("cid-ref");
            var byName = await Sut().GetIdentityProviderAsync("google-cid-ref");

            foreach (var read in new[] { byId, byClientId, byName })
            {
                read.Should().NotBeNull();
                read!.ClientSecretReference.Should().Be("cs-ref");
                read.PrivateKeyReference.Should().Be("pk-ref");
                read.ClientSecret.Should().BeEmpty();
                read.PrivateKey.Should().BeNull();
            }

            var raw = Raw(created.ItemId);
            raw["ClientSecretReference"].AsString.Should().Be("cs-ref");
            raw["PrivateKeyReference"].AsString.Should().Be("pk-ref");
        }

        [RealMongoFact]
        public async Task ReferenceFields_SurviveAFullReplace()
        {
            var created = await Sut().CreateIdentityProviderAsync(Provider("cid-upd", null, null));
            var loaded = (await Sut().GetIdentityProviderByIdAsync(created.ItemId))!;
            loaded.ClientSecret = string.Empty;
            loaded.ClientSecretReference = "cs-new";

            await Sut().UpdateIdentityProviderAsync(loaded);

            var read = await Sut().GetIdentityProviderByIdAsync(created.ItemId);
            read!.ClientSecretReference.Should().Be("cs-new");
            read.ClientSecret.Should().BeEmpty();
        }

        [RealMongoFact]
        public async Task LegacyProvider_StoresNoReferenceFields()
        {
            // [BsonIgnoreIfNull]: documents without a reference are written as before.
            var created = await Sut().CreateIdentityProviderAsync(Provider("cid-legacy", null, null));

            var raw = Raw(created.ItemId);
            raw.Contains("ClientSecretReference").Should().BeFalse();
            raw.Contains("PrivateKeyReference").Should().BeFalse();
            (await Sut().GetIdentityProviderByIdAsync(created.ItemId))!.ClientSecret.Should().Be("legacy-secret");
        }
    }
}
